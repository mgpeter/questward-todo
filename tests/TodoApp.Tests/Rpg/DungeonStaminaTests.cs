using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Services;
using TodoApp.Api.Services.Rpg;
using TodoApp.Data;
using TodoApp.Models;
using TodoApp.Models.Dice;
using TodoApp.Models.Progression;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

/// <summary>
/// What a dungeon costs in real work, and who is allowed to spend it.
/// </summary>
/// <remarks>
/// DEC-003 is the reason the whole RPG exists: stamina is the gate that keeps the game a sink
/// for work done rather than a substitute for it. A dungeon is the first thing in the game that
/// opens several fights from one request, so it is also the first place where one unit of
/// stamina could be made to buy more than one fight. That would not read as a cheat when it
/// happened; it would read as a run that felt generous.
/// <para>
/// <see cref="DungeonRunTests.A_three_room_run_costs_three_stamina_and_pays_on_the_last_blow"/>
/// prices the happy path. These price the paths that are not straight: a refused entry, a run
/// walked out of, and a class whose perk can make a fight free.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class DungeonStaminaTests(PostgresFixture postgres)
{
    private sealed record Harness(
        TodoDbContext Db,
        CombatService Combat,
        DungeonService Dungeons,
        QuestService Quests,
        Guid UserId);

    private async Task<Harness> ArrangeAsync(
        IDiceRoller roller,
        string classKey = ClassCatalog.Fighter,
        int level = 2,
        int stamina = 20)
    {
        await postgres.ResetAsync();
        var user = await postgres.CreateUserAsync("test|hero");

        var db = postgres.CreateContext();
        var sheets = new CharacterSheetService(db);
        var loot = new LootService(db, roller);
        var quests = new QuestService(db, loot, new ChronicleService(db));
        var adventurer = new AdventurerService(db, sheets, loot);
        var combat = new CombatService(db, roller, sheets, loot, quests, new ChronicleService(db));

        // The same context for both, which is what production's scoped registration gives them.
        var dungeons = new DungeonService(db, roller, sheets, combat);

        await adventurer.ChooseClassAsync(user.Id, classKey, TestContext.Current.CancellationToken);

        var harness = new Harness(db, combat, dungeons, quests, user.Id);
        await ReachLevelAsync(harness, level);

        var character = await db.Characters.SingleAsync(c => c.UserId == user.Id);
        character.Stamina = stamina;
        await db.SaveChangesAsync();

        return harness;
    }

    /// <summary>
    /// Raises the character the only way anything is allowed to, by finishing real work.
    /// Nothing in the RPG layer may pay experience (DEC-012), and a test that reached in and
    /// assigned TotalXp would be the first thing in the repository to write it.
    /// </summary>
    private static async Task ReachLevelAsync(Harness harness, int level)
    {
        var gamification = new GamificationService(harness.Db, new AchievementEvaluator(), harness.Quests, new ChronicleService(harness.Db));

        while (true)
        {
            var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);

            if (LevelCurve.LevelForXp(character.TotalXp) >= level)
            {
                return;
            }

            var task = new TodoTask
            {
                UserId = harness.UserId,
                Title = "Real work",
                Difficulty = Difficulty.Epic
            };

            harness.Db.Tasks.Add(task);
            await harness.Db.SaveChangesAsync();

            await gamification.CompleteAsync(harness.UserId, task.Id, 0, default);
        }
    }

    private async Task<int> StaminaAsync(Harness harness) =>
        (await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId, TestContext.Current.CancellationToken))
        .Stamina;

    private async Task<int> FightsOpenedAsync(Harness harness) =>
        await harness.Db.Encounters.AsNoTracking()
            .CountAsync(e => e.UserId == harness.UserId, TestContext.Current.CancellationToken);

    /// <summary>
    /// The DEC-003 accounting, over a run that does not go straight: every fight a dungeon ever
    /// opened was paid for, once.
    /// </summary>
    /// <remarks>
    /// The three crooked paths in one test, because it is their sum that has to balance. An
    /// entry refused because a fight is already open must charge nothing, or hammering the
    /// button would drain a player who got nothing for it. A room walked out of must not hand
    /// the stamina back, or fleeing would be a free look at what the next room holds. And a run
    /// abandoned must bank nothing towards the next one.
    /// <para>
    /// Asserted as a count of encounter rows against stamina spent rather than as a sequence of
    /// balances, because that is the invariant in the form it would be violated: something that
    /// opened a fight without charging for it moves the two numbers apart no matter which path
    /// it took to get there.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_run_never_opens_more_fights_than_the_stamina_it_charged()
    {
        // Two d12 draws for the first chain, a fumbled exchange, a killing blow with its gold
        // and drop dice, then two more d12s for the second chain. Nothing charges a die for
        // entering a room, refusing one or walking out of one.
        var script = new SequenceDiceRoller(1, 1, 1, 1, 18, 4, 3, 100, 1, 1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller, stamina: 5);

        var first = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        Assert.True(first.Ok, first.Message);

        var runId = first.Value!.Run.Id;

        var opened = await harness.Dungeons.EnterAsync(
            harness.UserId, runId, TestContext.Current.CancellationToken);

        Assert.True(opened.Ok, opened.Message);
        Assert.Equal(4, await StaminaAsync(harness));

        // Asking again while the room is still open. This is the button being pressed twice.
        var again = await harness.Dungeons.EnterAsync(
            harness.UserId, runId, TestContext.Current.CancellationToken);

        Assert.False(again.Ok);
        Assert.Equal(RpgFailure.EncounterAlreadyActive, again.Failure);
        Assert.Equal(4, await StaminaAsync(harness));
        Assert.Equal(1, await FightsOpenedAsync(harness));

        var room = opened.Value!.Encounter!;

        // A round that settles nothing, so the room costs more than one exchange and still only
        // one unit of work.
        var exchange = await harness.Combat.AttackAsync(
            harness.UserId, room.Id, TestContext.Current.CancellationToken);

        Assert.True(exchange.Ok, exchange.Message);
        Assert.Equal(EncounterStatus.Active, exchange.Value!.Encounter.Status);
        Assert.Equal(4, await StaminaAsync(harness));

        room.MonsterHitPoints = 1;
        await harness.Db.SaveChangesAsync();

        var kill = await harness.Combat.AttackAsync(
            harness.UserId, room.Id, TestContext.Current.CancellationToken);

        Assert.Equal(EncounterStatus.Won, kill.Value!.Encounter.Status);
        Assert.Equal(4, await StaminaAsync(harness));

        var second = await harness.Dungeons.EnterAsync(
            harness.UserId, runId, TestContext.Current.CancellationToken);

        Assert.True(second.Ok, second.Message);
        Assert.Equal(3, await StaminaAsync(harness));

        // Walking out with a room open. The fight ends, the run ends, and nothing comes back.
        var abandoned = await harness.Dungeons.AbandonAsync(
            harness.UserId, runId, TestContext.Current.CancellationToken);

        Assert.True(abandoned.Ok, abandoned.Message);
        Assert.Equal(DungeonRunStatus.Abandoned, abandoned.Value!.Run.Status);
        Assert.Equal(3, await StaminaAsync(harness));

        // And the next run starts from where the last one left the ledger, not from where it
        // started.
        var third = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        Assert.True(third.Ok, third.Message);

        var reopened = await harness.Dungeons.EnterAsync(
            harness.UserId, third.Value!.Run.Id, TestContext.Current.CancellationToken);

        Assert.True(reopened.Ok, reopened.Message);

        var stamina = await StaminaAsync(harness);
        var fights = await FightsOpenedAsync(harness);

        Assert.Equal(2, stamina);
        Assert.Equal(3, fights);

        // The whole point, in one line: five units of work went in, three fights came out, and
        // the two numbers agree.
        Assert.Equal(fights, 5 - stamina);

        Assert.Equal(10, script.RollCount);
        Assert.Equal([12, 12, 20, 20, 20, 8, 5, 100, 12, 12], roller.Sides);
    }

    /// <summary>
    /// A Wizard with nothing in the ledger is refused without the perk ever being rolled.
    /// </summary>
    /// <remarks>
    /// Arcane Recovery refunds a stamina, and there is nothing to refund at zero. Rolled before
    /// the gate instead of after it, the perk became a lottery ticket with no ticket price: a
    /// refused entry writes nothing at all, so a Wizard on zero stamina could post the same
    /// enter request in a loop and open a room free on five faces in twenty, then clear the run,
    /// its gold and its guaranteed Epic-or-better reward for no work at all. That is DEC-003's
    /// gate and DEC-015's five-rooms-five-stamina ruling defeated by a retry.
    /// <para>
    /// Asserted at the dice rather than only at the ledger, because a refusal that still drew a
    /// d20 is the whole bug: the ledger reads zero either way, and it is the fresh roll on every
    /// retry that turns a one-in-four perk into a certainty.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_wizard_with_no_stamina_is_refused_without_rolling_the_perk()
    {
        // Two d12s for the chain and nothing else. A d20 asked for here is the defect.
        var script = new SequenceDiceRoller(1, 1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller, ClassCatalog.Wizard, stamina: 0);

        var started = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        Assert.True(started.Ok, started.Message);

        // Three attempts, because one refusal proves nothing about a loop. Each is a fresh draw
        // in production, so a perk rolled ahead of the gate opens a room here sooner or later.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var refused = await harness.Dungeons.EnterAsync(
                harness.UserId, started.Value!.Run.Id, TestContext.Current.CancellationToken);

            Assert.False(refused.Ok);
            Assert.Equal(RpgFailure.NotEnoughStamina, refused.Failure);
        }

        Assert.Equal(0, await StaminaAsync(harness));
        Assert.Equal(0, await FightsOpenedAsync(harness));

        // The two chain draws and not a die more. The script would have thrown on a fourth roll;
        // this says which rolls happened rather than only that the script survived.
        Assert.Equal(2, script.RollCount);
        Assert.Equal([12, 12], roller.Sides);
    }

    /// <summary>
    /// A room cannot open on a run that was abandoned while the room was opening.
    /// </summary>
    /// <remarks>
    /// The run's liveness was read once, at the door, and then five awaited round trips passed
    /// before the fight committed. An abandon landing inside that window left an Active encounter
    /// pointing at a finished run: one stamina spent, the dungeon screen answering 204 so the run
    /// vanished, and ResolveDungeonClearAsync returning early forever after, so the rooms already
    /// won could never pay the clear reward.
    /// <para>
    /// The interleaving is staged rather than raced: the run is handed to the fight from one
    /// context while another context commits the abandon, which is exactly the state two
    /// concurrent requests produce and is the same state every time it runs.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_room_cannot_open_on_a_run_abandoned_while_it_was_opening()
    {
        var script = new SequenceDiceRoller(1, 1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller, stamina: 5);

        var started = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        var run = started.Value!.Run;

        // The other request, committing inside the window. A second context because that is what
        // two requests have: this one's tracked run still reads Active.
        await using (var other = postgres.CreateContext())
        {
            var same = await other.DungeonRuns.SingleAsync(
                r => r.Id == run.Id, TestContext.Current.CancellationToken);

            same.Status = DungeonRunStatus.Abandoned;
            same.EndedAt = DateTimeOffset.UtcNow;

            await other.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(DungeonRunStatus.Active, run.Status);

        var refused = await harness.Combat.StartAsync(
            harness.UserId, MonsterCatalog.GiantRat, run, TestContext.Current.CancellationToken);

        Assert.False(refused.Ok);
        Assert.Equal(RpgFailure.DungeonOver, refused.Failure);

        // Nothing was taken and nothing was opened, so there is no fight left holding the one
        // encounter slot against a run that can never use it.
        Assert.Equal(5, await StaminaAsync(harness));
        Assert.Equal(0, await FightsOpenedAsync(harness));

        Assert.Equal(2, script.RollCount);
        Assert.Equal([12, 12], roller.Sides);
    }

    /// <summary>
    /// The Wizard's free fight is rolled again for every room, and never banked across them.
    /// </summary>
    /// <remarks>
    /// Arcane Recovery is the one thing in the game allowed to open a fight for nothing, and a
    /// dungeon is where that permission is dangerous: a run reads as one action, so a perk
    /// evaluated once at the door and remembered would turn a single lucky d20 into a whole free
    /// dungeon. That is not a rounding error, it is a five room run bought with no work at all.
    /// <para>
    /// Two openings, scripted so the first is free and the second is not. One unit of stamina
    /// leaves the ledger, which is only possible if the perk was asked a second time.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_wizards_recovery_is_rolled_for_every_room_and_never_banked()
    {
        // Two d12s for each chain, and one d20 for each room opened. 18 clears the threshold of
        // 16 and 1 does not.
        var script = new SequenceDiceRoller(1, 1, 18, 1, 1, 1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller, ClassCatalog.Wizard, stamina: 3);

        var first = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        var free = await harness.Dungeons.EnterAsync(
            harness.UserId, first.Value!.Run.Id, TestContext.Current.CancellationToken);

        Assert.True(free.Ok, free.Message);
        Assert.NotNull(free.Value!.Encounter);

        // The room opened and the ledger did not move. That is the perk working.
        Assert.Equal(3, await StaminaAsync(harness));

        await harness.Dungeons.AbandonAsync(
            harness.UserId, first.Value.Run.Id, TestContext.Current.CancellationToken);

        var second = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        var paid = await harness.Dungeons.EnterAsync(
            harness.UserId, second.Value!.Run.Id, TestContext.Current.CancellationToken);

        Assert.True(paid.Ok, paid.Message);

        // Asked again, answered no, and charged. A perk that stuck would still read 3 here.
        Assert.Equal(2, await StaminaAsync(harness));
        Assert.Equal(2, await FightsOpenedAsync(harness));

        Assert.Equal(6, script.RollCount);
        Assert.Equal([12, 12, 20, 12, 12, 20], roller.Sides);
    }
}

/// <summary>
/// A run belongs to the person who paid for it, all the way down to the fight inside the room.
/// </summary>
/// <remarks>
/// The run routes are already covered: another user's run answers a start, an entry and an
/// abandon exactly as a run that never existed does. What those cannot cover is the encounter a
/// room opens, because a room's fight is an ordinary encounter row and is therefore reached
/// through the ordinary fight routes rather than through any dungeon route at all. A dungeon run
/// advanced by somebody else would not need a hole in the dungeon endpoints; it would only need
/// a hole in the attack endpoint.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class DungeonRoomIsolationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private QuestwardAppFactory _factory = null!;
    private HttpClient _alice = null!;
    private HttpClient _bob = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        _factory = new QuestwardAppFactory(postgres.ConnectionString);
        _alice = _factory.CreateClientAs("auth0|alice");
        _bob = _factory.CreateClientAs("auth0|bob");
    }

    public ValueTask DisposeAsync()
    {
        _alice.Dispose();
        _bob.Dispose();
        _factory.Dispose();

        return ValueTask.CompletedTask;
    }

    private static async Task ChooseClassAsync(HttpClient client) =>
        (await client.PutAsJsonAsync("/api/rpg/class", new { classKey = ClassCatalog.Fighter }))
            .EnsureSuccessStatusCode();

    /// <summary>Real work, enough of it to open the shallowest dungeon.</summary>
    private static async Task ReachDungeonLevelAsync(HttpClient client)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var created = await client.PostAsJsonAsync(
                "/api/tasks", new { title = $"Real work {attempt}", difficulty = "epic" });

            var task = await created.Content.ReadFromJsonAsync<IdDto>();

            (await client.PostAsJsonAsync(
                $"/api/tasks/{task!.Id}/complete", new { utcOffsetMinutes = 0 }))
                .EnsureSuccessStatusCode();

            var character = await client.GetFromJsonAsync<CharacterDto>("/api/character");

            if (character!.Level >= 2)
            {
                return;
            }
        }

        throw new InvalidOperationException("Ten Epic tasks did not reach level two.");
    }

    /// <summary>
    /// Puts a draught in a bag directly, because the shop is the only route to one and its shelf
    /// is a function of a user id the test cannot choose.
    /// </summary>
    private async Task<Guid> StockAsync(string subject, int quantity)
    {
        await using var db = postgres.CreateContext();

        var userId = await db.Users
            .Where(u => u.Auth0Sub == subject)
            .Select(u => u.Id)
            .SingleAsync(TestContext.Current.CancellationToken);

        var item = new InventoryItem
        {
            UserId = userId,
            ItemKey = ItemCatalog.DraughtOfMending,
            Slot = ItemSlot.Consumable,
            Rarity = Rarity.Common,
            Quantity = quantity
        };

        db.InventoryItems.Add(item);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return item.Id;
    }

    /// <summary>
    /// Nobody advances somebody else's run, including through the fight standing in it.
    /// </summary>
    /// <remarks>
    /// Every route that can move a room forward is tried against a stranger's room: swinging in
    /// it, drinking in it, and walking out of it. All three answer 404 rather than 403, so a room
    /// cannot be probed for existence either, and the run behind it is exactly where its owner
    /// left it afterwards.
    /// </remarks>
    [Fact]
    public async Task Another_adventurer_cannot_swing_in_a_room_that_is_not_theirs()
    {
        await ChooseClassAsync(_alice);
        await ChooseClassAsync(_bob);
        await ReachDungeonLevelAsync(_alice);
        await ReachDungeonLevelAsync(_bob);

        var started = await _alice.PostAsJsonAsync(
            "/api/rpg/dungeons", new { dungeonKey = DungeonCatalog.SunkenWarren });

        Assert.Equal(HttpStatusCode.Created, started.StatusCode);

        var run = (await started.Content.ReadFromJsonAsync<DungeonRunDto>())!;

        var entered = await _alice.PostAsync($"/api/rpg/dungeons/{run.Id}/enter", null);

        entered.EnsureSuccessStatusCode();

        var opened = (await entered.Content.ReadFromJsonAsync<DungeonRunDto>())!;
        var room = opened.Encounter!;

        var draught = await StockAsync("auth0|bob", quantity: 2);
        var staminaBefore = (await _bob.GetFromJsonAsync<SheetDto>("/api/rpg/sheet"))!.Stamina;

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _bob.PostAsync($"/api/rpg/encounters/{room.Id}/attack", null)).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _bob.PostAsync($"/api/rpg/encounters/{room.Id}/use/{draught}", null)).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _bob.PostAsync($"/api/rpg/encounters/{room.Id}/flee", null)).StatusCode);

        // Nothing of Bob's moved either: no stamina, and the draught is still a stack of two.
        Assert.Equal(staminaBefore, (await _bob.GetFromJsonAsync<SheetDto>("/api/rpg/sheet"))!.Stamina);

        var bag = (await _bob.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory"))!;

        Assert.Equal(2, Assert.Single(bag, i => i.Id == draught).Quantity);

        // And Alice's room is untouched: the same fight, still open, still on round zero.
        var mine = (await _alice.GetFromJsonAsync<DungeonRunDto>("/api/rpg/dungeons/active"))!;

        Assert.Equal(run.Id, mine.Id);
        Assert.Equal(0, mine.Depth);
        Assert.Equal(room.Id, mine.Encounter!.Id);
        Assert.Equal("active", mine.Encounter.Status);
        Assert.Equal(0, mine.Encounter.Round);
    }

    private sealed record IdDto(Guid Id);

    private sealed record CharacterDto(int Level, int TotalXp);

    private sealed record SheetDto(int Stamina);

    private sealed record ItemDto(Guid Id, string ItemKey, int Quantity);

    private sealed record EncounterDto(Guid Id, string MonsterKey, string Status, int Round, List<StatusEffectDto> Effects);

    private sealed record StatusEffectDto(string Kind, string Target, int Rounds, int Magnitude, string Source);

    private sealed record DungeonRunDto(
        Guid Id,
        string DungeonKey,
        string Status,
        int Depth,
        EncounterDto? Encounter);
}
