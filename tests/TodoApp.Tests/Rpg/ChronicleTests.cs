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
/// The journal, written by the events themselves.
/// </summary>
/// <remarks>
/// Every test here provokes a real event through the service that owns it rather than inserting a
/// row, because the thing worth guarding is the wiring: an entry that has to be remembered at the
/// call site is one that will be forgotten at the next one.
/// <para>
/// One <see cref="TodoDbContext"/> shared by every service, matching the scoped registrations, so
/// an entry lands in the same transaction as the thing it records. A harness that gave each
/// service its own context would prove nothing about that.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class ChronicleTests(PostgresFixture postgres)
{
    private static SequenceDiceRoller AlwaysHits() => new(Enumerable.Repeat(20, 4000).ToArray());

    private sealed record Harness(
        TodoDbContext Db,
        ChronicleService Chronicle,
        CombatService Combat,
        HuntService Hunts,
        QuestService Quests,
        AdventurerService Adventurer,
        GamificationService Gamification,
        Guid UserId);

    private async Task<Harness> ArrangeAsync(int stamina = 20)
    {
        await postgres.ResetAsync();
        var user = await postgres.CreateUserAsync("test|chronicler");

        var db = postgres.CreateContext();
        var roller = AlwaysHits();
        var sheets = new CharacterSheetService(db);
        var loot = new LootService(db, roller);
        var chronicle = new ChronicleService(db);
        var quests = new QuestService(db, loot, chronicle);
        var adventurer = new AdventurerService(db, sheets, loot);
        var combat = new CombatService(db, roller, sheets, loot, quests, chronicle);
        var hunts = new HuntService(db, sheets, combat, chronicle);
        var gamification = new GamificationService(db, new AchievementEvaluator(), quests, chronicle);

        await adventurer.ChooseClassAsync(user.Id, ClassCatalog.Fighter, default);

        var character = await db.Characters.SingleAsync(c => c.UserId == user.Id);
        character.Stamina = stamina;
        await db.SaveChangesAsync();

        return new Harness(
            db, chronicle, combat, hunts, quests, adventurer, gamification, user.Id);
    }

    private static Task<List<ChronicleEntry>> EntriesAsync(Harness harness) =>
        harness.Db.ChronicleEntries
            .AsNoTracking()
            .Where(e => e.UserId == harness.UserId)
            .OrderByDescending(e => e.OccurredAt)
            .ToListAsync(TestContext.Current.CancellationToken);

    private static async Task<TodoTask> AddTaskAsync(
        Harness harness,
        string title = "File the tax return",
        int daysOverdue = 0,
        string[]? tags = null)
    {
        var task = new TodoTask
        {
            UserId = harness.UserId,
            Title = title,
            Difficulty = Difficulty.Epic,
            Tags = [.. tags ?? []],
            DueDate = daysOverdue > 0
                ? DateTimeOffset.UtcNow.AddDays(-daysOverdue).AddHours(-1)
                : null
        };

        harness.Db.Tasks.Add(task);
        await harness.Db.SaveChangesAsync();

        return task;
    }

    private static async Task<AttackOutcome> WinTheFightAsync(Harness harness, Guid encounterId)
    {
        var sheets = new CharacterSheetService(harness.Db);

        for (var round = 0; round < 200; round++)
        {
            var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);
            var sheet = await sheets.BuildAsync(character, default);

            character.CurrentHitPoints = sheet.MaxHitPoints;
            character.HitPointsUpdatedAt = DateTimeOffset.UtcNow;
            await harness.Db.SaveChangesAsync();

            var attack = await harness.Combat.AttackAsync(harness.UserId, encounterId, default);

            Assert.True(attack.Ok, attack.Message);

            if (attack.Value!.Encounter.IsOver)
            {
                return attack.Value;
            }
        }

        throw new Xunit.Sdk.XunitException("Two hundred guaranteed hits did not finish the fight.");
    }

    // -------------------------------------------------------------------------
    // One line per thing that happened
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_won_fight_is_written_down_with_the_encounter_it_came_from()
    {
        var harness = await ArrangeAsync();

        var started = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.All[0].Key, default);
        Assert.True(started.Ok, started.Message);

        await WinTheFightAsync(harness, started.Value!.Id);

        var entry = Assert.Single(await EntriesAsync(harness), e => e.Kind == ChronicleKind.FightWon);

        Assert.Equal(started.Value.Id, entry.EncounterId);

        var facts = ChronicleService.ReadFacts(entry);

        Assert.Equal(MonsterCatalog.All[0].Key, facts[ChronicleNarrator.MonsterKey]);
        Assert.Equal(ChronicleNarrator.IconFight, Narrate(entry).Icon);
    }

    [Fact]
    public async Task Walking_out_of_a_fight_is_written_down_as_a_withdrawal()
    {
        var harness = await ArrangeAsync();

        var started = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.All[0].Key, default);
        await harness.Combat.FleeAsync(harness.UserId, started.Value!.Id, default);

        var entry = Assert.Single(await EntriesAsync(harness), e => e.Kind == ChronicleKind.FightFled);

        Assert.Equal(started.Value.Id, entry.EncounterId);
    }

    [Fact]
    public async Task Taking_a_contract_is_written_down_before_any_fight()
    {
        var harness = await ArrangeAsync();
        var task = await AddTaskAsync(harness, "Renew the passport", daysOverdue: 3, tags: ["admin"]);

        await harness.Hunts.AcceptAsync(harness.UserId, task.Id, default);

        var entry = Assert.Single(
            await EntriesAsync(harness), e => e.Kind == ChronicleKind.ContractAccepted);

        var facts = ChronicleService.ReadFacts(entry);

        Assert.Equal("Renew the passport", facts[ChronicleNarrator.TaskTitleKey]);
        Assert.Equal(FactionCatalog.TheLedger, facts[ChronicleNarrator.FactionKey]);

        // No fight has happened, so nothing else has been written and nothing points at an
        // encounter. The contract beat is worth recording on its own precisely because it may
        // never become one.
        Assert.Null(entry.EncounterId);
    }

    [Fact]
    public async Task Settling_a_contract_names_the_banner_and_records_the_standing_it_raised()
    {
        var harness = await ArrangeAsync();
        var task = await AddTaskAsync(harness, "File the tax return", daysOverdue: 4, tags: ["work"]);

        await harness.Hunts.AcceptAsync(harness.UserId, task.Id, default);
        await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);
        await harness.Hunts.DischargeAsync(harness.UserId, task.Id, default);

        var contract = await harness.Db.HuntContracts
            .AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId, TestContext.Current.CancellationToken);

        var fight = await harness.Hunts.FightAsync(harness.UserId, contract.Id, default);
        Assert.True(fight.Ok, fight.Message);

        await WinTheFightAsync(harness, fight.Value!.Encounter.Id);

        var entries = await EntriesAsync(harness);

        var settled = Assert.Single(entries, e => e.Kind == ChronicleKind.ContractSettled);
        var settledFacts = ChronicleService.ReadFacts(settled);

        Assert.Equal(FactionCatalog.TheLedger, settledFacts[ChronicleNarrator.FactionKey]);
        Assert.Equal("File the tax return", settledFacts[ChronicleNarrator.TaskTitleKey]);

        // The first win under a banner moves Unknown to Noticed, so the tier line is written
        // beside it. Standing is counted from the fights, so what is asserted here is the
        // counting, not a stored number.
        var raised = Assert.Single(entries, e => e.Kind == ChronicleKind.StandingRaised);
        var raisedFacts = ChronicleService.ReadFacts(raised);

        Assert.Equal(
            ((int)FactionStanding.Noticed).ToString(),
            raisedFacts[ChronicleNarrator.StandingKey]);
        Assert.Equal("1", raisedFacts[ChronicleNarrator.WinsKey]);
    }

    [Fact]
    public async Task Reaching_a_level_is_written_down_once_for_the_crossing()
    {
        var harness = await ArrangeAsync();

        // Level 2 is 50 experience, so two Epics cross it and the third does not cross anything.
        for (var i = 0; i < 3; i++)
        {
            var task = await AddTaskAsync(harness, $"Real work {i}");
            await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);
        }

        var levels = (await EntriesAsync(harness))
            .Where(e => e.Kind == ChronicleKind.LevelReached)
            .Select(e => ChronicleService.ReadFacts(e)[ChronicleNarrator.LevelKey])
            .ToList();

        var character = await harness.Db.Characters
            .AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId, TestContext.Current.CancellationToken);

        Assert.Equal(LevelCurve.LevelForXp(character.TotalXp), int.Parse(levels[0]));
        Assert.Equal(levels.Distinct().Count(), levels.Count);
    }

    // -------------------------------------------------------------------------
    // Reading it back
    // -------------------------------------------------------------------------

    [Fact]
    public async Task History_pages_back_on_the_timestamp_and_filters_by_kind()
    {
        var harness = await ArrangeAsync();
        var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);

        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 5; i++)
        {
            harness.Chronicle.Record(
                character,
                i % 2 == 0 ? ChronicleKind.FightWon : ChronicleKind.QuestClaimed,
                new Dictionary<string, string> { [ChronicleNarrator.MonsterKey] = "goblin" },
                at: now.AddMinutes(-i));
        }

        await harness.Db.SaveChangesAsync();

        var first = await harness.Chronicle.HistoryAsync(harness.UserId, 2, null, null, default);

        Assert.Equal(2, first.Count);
        Assert.True(first[0].OccurredAt > first[1].OccurredAt);

        var second = await harness.Chronicle.HistoryAsync(
            harness.UserId, 2, first[^1].OccurredAt, null, default);

        Assert.Equal(2, second.Count);
        Assert.True(second[0].OccurredAt < first[^1].OccurredAt);

        var quests = await harness.Chronicle.HistoryAsync(
            harness.UserId, 20, null, ChronicleKind.QuestClaimed, default);

        Assert.Equal(2, quests.Count);
        Assert.All(quests, e => Assert.Equal(ChronicleKind.QuestClaimed, e.Kind));
    }

    /// <summary>
    /// The line outlives the fight it describes, which is the whole reason it is a row.
    /// </summary>
    /// <remarks>
    /// Deleting the encounter directly rather than ascending, so this asserts the referential
    /// action itself: <c>ExecuteDeleteAsync</c> bypasses the change tracker, so SET NULL on the
    /// foreign key is the only thing standing between an ascension and an empty history.
    /// </remarks>
    [Fact]
    public async Task An_entry_survives_the_encounter_it_points_at()
    {
        var harness = await ArrangeAsync();

        var started = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.All[0].Key, default);
        await harness.Combat.FleeAsync(harness.UserId, started.Value!.Id, default);

        await harness.Db.Encounters
            .Where(e => e.UserId == harness.UserId)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(await EntriesAsync(harness));

        Assert.Null(entry.EncounterId);

        // Still a sentence, because the narration reads the facts on the row and never the fight.
        Assert.Contains(MonsterNames.Of(MonsterCatalog.All[0].Key), Narrate(entry).Title);
    }

    [Fact]
    public async Task One_chronicle_is_invisible_to_another_user()
    {
        var harness = await ArrangeAsync();
        var stranger = await postgres.CreateUserAsync("test|stranger");

        var started = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.All[0].Key, default);
        await harness.Combat.FleeAsync(harness.UserId, started.Value!.Id, default);

        var theirs = await harness.Chronicle.HistoryAsync(stranger.Id, 20, null, null, default);

        Assert.Empty(theirs);
        Assert.NotEmpty(await harness.Chronicle.HistoryAsync(harness.UserId, 20, null, null, default));
    }

    private static ChronicleLine Narrate(ChronicleEntry entry) =>
        ChronicleNarrator.Narrate(entry.Kind, ChronicleService.ReadFacts(entry));
}
