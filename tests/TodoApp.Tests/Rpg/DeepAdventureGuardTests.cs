using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Services.Rpg;
using TodoApp.Data;
using TodoApp.Models.Dice;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

/// <summary>
/// The standing rules, re-asserted against everything this phase added.
/// </summary>
/// <remarks>
/// Two of the repository's decisions are the kind that are kept by tests rather than by review,
/// and both got new surface area in this phase. DEC-012 says nothing in the RPG layer may pay
/// experience, and there are now six more routes that could. DEC-004 says catalogs live in code
/// and only keys are persisted, and consumables are the first thing whose row carries a count
/// that a craft could quietly multiply.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class DeepAdventureGuardTests(PostgresFixture postgres) : IAsyncLifetime
{
    private QuestwardAppFactory _factory = null!;
    private HttpClient _alice = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        _factory = new QuestwardAppFactory(postgres.ConnectionString);
        _alice = _factory.CreateClientAs("auth0|alice");
    }

    public ValueTask DisposeAsync()
    {
        _alice.Dispose();
        _factory.Dispose();

        return ValueTask.CompletedTask;
    }

    private static async Task ChooseClassAsync(HttpClient client) =>
        (await client.PutAsJsonAsync("/api/rpg/class", new { classKey = ClassCatalog.Fighter }))
            .EnsureSuccessStatusCode();

    /// <summary>
    /// Real work, enough of it to open the shallowest dungeon and pay for several rooms.
    /// </summary>
    /// <remarks>
    /// Everything experience-bearing in the whole test happens here, before the snapshot is
    /// taken. Nothing after this line is allowed to move either number.
    /// </remarks>
    private static async Task WorkToLevelTwoAsync(HttpClient client)
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

            // One more than the gate needs, so there is stamina for every room of the run.
            if (character!.Level >= 2 && attempt >= 1)
            {
                return;
            }
        }

        throw new InvalidOperationException("Ten Epic tasks did not reach level two.");
    }

    private async Task<Guid> StockAsync(string itemKey, int quantity)
    {
        await using var db = postgres.CreateContext();

        var userId = await db.Users
            .Where(u => u.Auth0Sub == "auth0|alice")
            .Select(u => u.Id)
            .SingleAsync(TestContext.Current.CancellationToken);

        var item = new InventoryItem
        {
            UserId = userId,
            ItemKey = itemKey,
            Slot = ItemSlot.Consumable,
            Rarity = Rarity.Common,
            Quantity = quantity
        };

        db.InventoryItems.Add(item);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return item.Id;
    }

    /// <summary>
    /// Arms whatever fight is open with every kind of effect, so the tick path runs for real.
    /// </summary>
    /// <remarks>
    /// Written straight onto the row rather than played through an item, because the wire runs
    /// on real randomness: there is no way from out here to make a boss reach a threshold or a
    /// draught land a particular effect, and this guard has to see the tick actually fire rather
    /// than hope it did.
    /// </remarks>
    private async Task ArmActiveFightAsync()
    {
        await using var db = postgres.CreateContext();

        var encounter = await db.Encounters
            .FirstOrDefaultAsync(e => e.Status == EncounterStatus.Active, TestContext.Current.CancellationToken);

        if (encounter is null)
        {
            return;
        }

        StatusEffects.Write(
            encounter,
            [
                new StatusEffect(EffectKind.Poisoned, EffectTarget.Monster, 3, 2, "test"),
                new StatusEffect(EffectKind.Regenerating, EffectTarget.Player, 3, 2, "test"),
                new StatusEffect(EffectKind.Empowered, EffectTarget.Player, 3, 2, "test"),
                new StatusEffect(EffectKind.Guarded, EffectTarget.Player, 3, 2, "test"),
                new StatusEffect(EffectKind.Weakened, EffectTarget.Monster, 3, 0, "test")
            ]);

        // The gear a fight has entered is scratch state on the row, and the phase routes read it
        // back. Set here so the wire reports one and the client path is exercised, even though
        // no monster this character can reach at this level has a gear of its own.
        encounter.Phase = 1;

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Swings until the fight resolves, or gives up. Returns the last status seen.</summary>
    private async Task<string> FightToTheEndAsync(Guid encounterId)
    {
        var status = "active";

        for (var round = 0; round < 30 && status == "active"; round++)
        {
            var attack = await _alice.PostAsync($"/api/rpg/encounters/{encounterId}/attack", null);

            if (!attack.IsSuccessStatusCode)
            {
                break;
            }

            status = (await attack.Content.ReadFromJsonAsync<AttackDto>())!.Encounter.Status;
        }

        return status;
    }

    /// <summary>
    /// DEC-012, re-asserted over every route this phase added.
    /// </summary>
    /// <remarks>
    /// The rule is not that combat does not award experience; it is that nothing in the RPG
    /// layer does. Each of these routes is a fresh chance to break it, and the ones that pay
    /// something are the dangerous ones: a dungeon clear pays gold and an item, a draught pays
    /// hit points, and each of those is one line away from paying experience too by copying the
    /// wrong reward helper.
    /// <para>
    /// Everything runs on the production dice, so no outcome here is scripted and none is
    /// asserted. What is asserted is the pair of numbers that must not move whatever happens:
    /// the character wins rooms, loses rooms, drinks, is poisoned, regenerates, walks out of a
    /// dungeon and enters a gear, and comes out with the experience the tasks bought and not one
    /// point more.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task No_route_added_in_this_phase_can_move_experience_or_level()
    {
        await ChooseClassAsync(_alice);

        // Enough for level two and a good many rooms. Every point of experience in this test is
        // earned right here.
        await WorkToLevelTwoAsync(_alice);

        var draughts = await StockAsync(ItemCatalog.DraughtOfMending, quantity: 3);
        var vials = await StockAsync(ItemCatalog.VialOfSerpentsKiss, quantity: 3);

        var before = (await _alice.GetFromJsonAsync<CharacterDto>("/api/character"))!;

        Assert.True(before.Level >= 2, "the work done did not open the shallowest dungeon");

        // The reading routes first. None of them should so much as touch a character row.
        (await _alice.GetAsync("/api/rpg/dungeons")).EnsureSuccessStatusCode();
        (await _alice.GetAsync("/api/rpg/dungeons/active")).EnsureSuccessStatusCode();

        var started = await _alice.PostAsJsonAsync(
            "/api/rpg/dungeons", new { dungeonKey = DungeonCatalog.SunkenWarren });

        Assert.Equal(HttpStatusCode.Created, started.StatusCode);

        var run = (await started.Content.ReadFromJsonAsync<DungeonRunDto>())!;
        var status = "active";

        for (var room = 0; room < 3 && status == "active"; room++)
        {
            var entered = await _alice.PostAsync($"/api/rpg/dungeons/{run.Id}/enter", null);

            if (!entered.IsSuccessStatusCode)
            {
                break;
            }

            var opened = (await entered.Content.ReadFromJsonAsync<DungeonRunDto>())!;
            var fight = opened.Encounter!;

            // Effects riding a room, a draught drunk inside one, and a poison thrown in it.
            await ArmActiveFightAsync();

            await _alice.PostAsync($"/api/rpg/encounters/{fight.Id}/use/{draughts}", null);
            await _alice.PostAsync($"/api/rpg/encounters/{fight.Id}/use/{vials}", null);

            status = await FightToTheEndAsync(fight.Id);

            var active = await _alice.GetAsync("/api/rpg/dungeons/active");

            status = active.StatusCode == HttpStatusCode.NoContent ? "over" : "active";
        }

        // Whatever state the run reached, walking out is a route too.
        await _alice.PostAsync($"/api/rpg/dungeons/{run.Id}/abandon", null);

        var after = (await _alice.GetFromJsonAsync<CharacterDto>("/api/character"))!;

        Assert.Equal(before.TotalXp, after.TotalXp);
        Assert.Equal(before.Level, after.Level);

        // And the routes did something, so the guard is guarding a path that ran rather than one
        // that bailed at the first request.
        var bag = (await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory"))!;
        var draughtsLeft = bag.SingleOrDefault(i => i.Id == draughts)?.Quantity ?? 0;

        Assert.True(draughtsLeft < 3, "no draught was ever drunk, so the use route was never exercised");
    }

    /// <summary>
    /// An effect applied in one request is still in force in the next one.
    /// </summary>
    /// <remarks>
    /// Effects live on the encounter row precisely so that a reload does not lose them, and this
    /// is the only test that takes that claim across a request boundary and a real database
    /// round trip. Everything else asserting a multi-round effect does so inside one service
    /// instance, where the array is the same list object throughout and a serialisation that
    /// dropped a field, or a column that never persisted, would go unnoticed.
    /// <para>
    /// The modifier is on the attack roll whether it lands or not, which is what lets this run
    /// on the production dice: nothing here depends on an outcome. The monster is taken from the
    /// board rather than named, so the level the work reached cannot put it out of band.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_effect_drunk_in_one_request_is_still_in_force_in_the_next()
    {
        await ChooseClassAsync(_alice);
        await WorkToLevelTwoAsync(_alice);

        var oil = await StockAsync(ItemCatalog.WhetstoneOil, quantity: 1);
        var board = (await _alice.GetFromJsonAsync<List<MonsterListDto>>("/api/rpg/monsters"))!;

        Assert.NotEmpty(board);

        var start = await _alice.PostAsJsonAsync(
            "/api/rpg/encounters", new { monsterKey = board[0].Key });

        Assert.Equal(HttpStatusCode.Created, start.StatusCode);

        var fight = (await start.Content.ReadFromJsonAsync<EncounterDto>())!;

        // Round one is spent oiling the blade. It buys three swings and no attack roll.
        var drunk = await _alice.PostAsync($"/api/rpg/encounters/{fight.Id}/use/{oil}", null);

        drunk.EnsureSuccessStatusCode();

        var oiled = (await drunk.Content.ReadFromJsonAsync<AttackDto>())!;

        Assert.Equal(1, oiled.Encounter.Round);
        Assert.DoesNotContain(oiled.Rolls, r => r.Actor == "player" && r.Kind == "attack");

        // Round two is a separate request, served by a separate unit of work, reading the effect
        // back out of the column it was written to.
        var swung = await _alice.PostAsync($"/api/rpg/encounters/{fight.Id}/attack", null);

        swung.EnsureSuccessStatusCode();

        var round = (await swung.Content.ReadFromJsonAsync<AttackDto>())!;
        var swing = Assert.Single(round.Rolls, r => r.Actor == "player" && r.Kind == "attack");

        Assert.Contains(swing.Modifiers, m => m.Label == "empowered" && m.Value == 1);

        // And the bag is down to nothing, because a draught is gone the moment it works.
        var bag = (await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory"))!;

        Assert.DoesNotContain(bag, i => i.Id == oil);
    }

    // The wire-shape tripwire. Hand written and deliberately partial: these bind by name, so a
    // field added to a response leaves them alone and a field renamed or retyped fails to
    // deserialise here rather than quietly reaching a client.
    private sealed record IdDto(Guid Id);

    private sealed record CharacterDto(int Level, int TotalXp);

    private sealed record ItemDto(Guid Id, string ItemKey, int Quantity);

    private sealed record MonsterListDto(string Key, string Name, int Level);

    private sealed record ModifierDto(string Label, int Value);

    private sealed record RollDto(string Actor, string Kind, List<ModifierDto> Modifiers);

    private sealed record EncounterDto(Guid Id, string Status, int Round, int Phase, string? PhaseName, List<StatusEffectDto> Effects);

    private sealed record StatusEffectDto(string Kind, string Target, int Rounds, int Magnitude, string Source);

    private sealed record AttackDto(EncounterDto Encounter, List<RollDto> Rolls);

    private sealed record DungeonRunDto(Guid Id, string Status, int Depth, EncounterDto? Encounter);
}

/// <summary>The forge, against the one kind of row that carries a count.</summary>
[Collection(nameof(PostgresCollection))]
public class ConsumableForgeTests(PostgresFixture postgres)
{
    private sealed record Harness(TodoDbContext Db, LootService Loot, ForgeService Forge, Guid UserId);

    private async Task<Harness> ArrangeAsync(IDiceRoller roller)
    {
        await postgres.ResetAsync();
        var user = await postgres.CreateUserAsync("test|hero");

        var db = postgres.CreateContext();
        var sheets = new CharacterSheetService(db);
        var loot = new LootService(db, roller);
        var adventurer = new AdventurerService(db, sheets, loot);

        await adventurer.ChooseClassAsync(
            user.Id, ClassCatalog.Fighter, TestContext.Current.CancellationToken);

        var character = await db.Characters.SingleAsync(c => c.UserId == user.Id);
        character.Essence = 10_000;
        await db.SaveChangesAsync();

        return new Harness(db, loot, new ForgeService(db, roller), user.Id);
    }

    /// <summary>
    /// The bench refuses a stack, at every rarity, and spends nothing finding that out.
    /// </summary>
    /// <remarks>
    /// The stacking key is the user, the item key and the rarity. An affix is not in that key,
    /// so two rows that differed only by a word would be one row to the database and two items
    /// to the player: the second acquisition would either lose to the unique index with a 500,
    /// or land on the first stack and silently take its words. Nothing must be able to put a
    /// word on a consumable, and the forge is the only route that could.
    /// <para>
    /// <c>AffixRules.RollableFor</c> returning zero for the slot is what makes that true, and
    /// <see cref="AffixAndSetTests"/> asserts the pool is empty. This asserts the route in front
    /// of it refuses, which is the half a player could actually reach: a rule that holds only
    /// because nothing calls it is one call away from not holding.
    /// </para>
    /// <para>
    /// The empty script is load-bearing. A refusal that had already drawn from the pool would
    /// change what the next paid craft was handed, so failing is not enough: it has to fail
    /// before the die.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(Rarity.Common)]
    [InlineData(Rarity.Uncommon)]
    [InlineData(Rarity.Rare)]
    [InlineData(Rarity.Epic)]
    [InlineData(Rarity.Legendary)]
    public async Task The_forge_cannot_put_a_word_on_a_stack(Rarity rarity)
    {
        var script = new SequenceDiceRoller();
        var harness = await ArrangeAsync(script);

        var stack = await harness.Loot.GrantAsync(
            harness.UserId, ItemCatalog.DraughtOfMending, rarity, TestContext.Current.CancellationToken);

        stack.Quantity = 6;
        await harness.Db.SaveChangesAsync();

        var imbued = await harness.Forge.ImbueAsync(
            harness.UserId, stack.Id, TestContext.Current.CancellationToken);

        Assert.False(imbued.Ok);
        Assert.Equal(RpgFailure.CannotUpgrade, imbued.Failure);

        var reforged = await harness.Forge.ReforgeAsync(
            harness.UserId, stack.Id, TestContext.Current.CancellationToken);

        Assert.False(reforged.Ok);
        Assert.Equal(RpgFailure.CannotUpgrade, reforged.Failure);

        // Untouched: no word, no essence, and the whole stack still there.
        var after = await harness.Db.InventoryItems.AsNoTracking()
            .SingleAsync(i => i.Id == stack.Id, TestContext.Current.CancellationToken);

        Assert.Null(after.PrefixKey);
        Assert.Null(after.SuffixKey);
        Assert.Equal(6, after.Quantity);
        Assert.Equal(ItemCatalog.Find(ItemCatalog.DraughtOfMending)!.Name, after.DisplayName);

        Assert.Equal(
            10_000,
            (await harness.Db.Characters.AsNoTracking()
                .SingleAsync(c => c.UserId == harness.UserId, TestContext.Current.CancellationToken))
            .Essence);

        // Refused before the pool, not after it.
        Assert.Equal(0, script.RollCount);
    }
}
