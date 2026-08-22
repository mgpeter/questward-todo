using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Services.Rpg;
using TodoApp.Data;
using TodoApp.Models;
using TodoApp.Models.Dice;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

/// <summary>
/// Lore is derived, never stored (DEC-002). There is no lore_unlocks table, so an unlock is a
/// pure function of the level, the bestiary counters and the claimed quests.
/// </summary>
/// <remarks>
/// That design is what makes "unlocked twice" impossible rather than merely unlikely, and it is
/// the property worth pinning: a table of unlocks could grow a second row for the same fragment
/// and nothing in the reading code would notice. These tests assert the derivation instead,
/// including that crossing a threshold repeatedly changes nothing.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class LoreTests(PostgresFixture postgres)
{
    private const string RatSighted = "giant-rat-sighted";
    private const string RatKnown = "giant-rat-known";
    private const string RatStudied = "giant-rat-studied";
    private const string TavernHouseRules = "tavern-house-rules";
    private const string TavernLongTable = "tavern-long-table";
    private const string TavernSlate = "tavern-slate";

    private static SequenceDiceRoller AlwaysHits() =>
        new(Enumerable.Repeat(20, 800).ToArray());

    private sealed record Harness(
        TodoDbContext Db,
        CombatService Combat,
        BestiaryService Bestiary,
        QuestService Quests,
        Guid UserId);

    private async Task<Harness> ArrangeAsync(IDiceRoller roller)
    {
        await postgres.ResetAsync();
        var user = await postgres.CreateUserAsync("test|hero");

        var db = postgres.CreateContext();
        var sheets = new CharacterSheetService(db);
        var loot = new LootService(db, roller);
        var quests = new QuestService(db, loot, new ChronicleService(db));
        var adventurer = new AdventurerService(db, sheets, loot);
        var combat = new CombatService(db, roller, sheets, loot, quests, new ChronicleService(db));

        await adventurer.ChooseClassAsync(user.Id, ClassCatalog.Fighter, TestContext.Current.CancellationToken);

        var character = await db.Characters.SingleAsync(c => c.UserId == user.Id);
        character.Stamina = 100;
        await db.SaveChangesAsync();

        return new Harness(db, combat, new BestiaryService(db), quests, user.Id);
    }

    private static async Task<IReadOnlyList<string>> UnlockedKeysAsync(Harness harness)
    {
        var collection = await harness.Bestiary.LoreAsync(harness.UserId, default);

        return collection.Places
            .SelectMany(p => p.Fragments)
            .Where(f => f.IsUnlocked)
            .Select(f => f.Fragment.Key)
            .ToList();
    }

    private async Task WinFightsAsync(Harness harness, string monsterKey, int count)
    {
        for (var fight = 0; fight < count; fight++)
        {
            var start = await harness.Combat.StartAsync(harness.UserId, monsterKey, default);
            Assert.True(start.Ok);

            for (var round = 0; round < 30; round++)
            {
                var attack = await harness.Combat.AttackAsync(harness.UserId, start.Value!.Id, default);

                if (attack.Value!.Encounter.IsOver)
                {
                    Assert.Equal(EncounterStatus.Won, attack.Value.Encounter.Status);
                    break;
                }
            }
        }
    }

    private async Task FleeFightsAsync(Harness harness, string monsterKey, int count)
    {
        for (var fight = 0; fight < count; fight++)
        {
            var start = await harness.Combat.StartAsync(harness.UserId, monsterKey, default);
            await harness.Combat.FleeAsync(harness.UserId, start.Value!.Id, default);
        }
    }

    // -------------------------------------------------------------------------
    // Nothing is free
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_new_character_has_the_whole_collection_and_almost_none_of_it()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var collection = await harness.Bestiary.LoreAsync(harness.UserId, default);

        Assert.Equal(LoreCatalog.Places.Count, collection.Places.Count);
        Assert.Equal(LoreCatalog.All.Count, collection.Total);

        // Level 1 is where everyone starts, so the level 1 fragment is open and it is the
        // only thing that is. Everything else has to be earned.
        var unlocked = await UnlockedKeysAsync(harness);

        Assert.Equal([TavernHouseRules], unlocked);
    }

    // -------------------------------------------------------------------------
    // The monster ladder
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_first_sighting_opens_the_field_note_and_nothing_further_up_the_ladder()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        await FleeFightsAsync(harness, MonsterCatalog.GiantRat, 1);

        var unlocked = await UnlockedKeysAsync(harness);

        Assert.Contains(RatSighted, unlocked);
        Assert.DoesNotContain(RatKnown, unlocked);
        Assert.DoesNotContain(RatStudied, unlocked);
    }

    [Fact]
    public async Task Sightings_alone_never_open_a_fragment_that_asks_for_kills()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        // Met five times and killed none of them. Seen and slain are separate counters for
        // exactly this reason.
        await FleeFightsAsync(harness, MonsterCatalog.GiantRat, 5);

        var unlocked = await UnlockedKeysAsync(harness);

        Assert.Contains(RatSighted, unlocked);
        Assert.DoesNotContain(RatKnown, unlocked);
    }

    [Fact]
    public async Task The_kill_ladder_opens_one_rung_at_a_time()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        await WinFightsAsync(harness, MonsterCatalog.GiantRat, 2);
        Assert.DoesNotContain(RatKnown, await UnlockedKeysAsync(harness));

        await WinFightsAsync(harness, MonsterCatalog.GiantRat, 1);
        var atThree = await UnlockedKeysAsync(harness);
        Assert.Contains(RatKnown, atThree);
        Assert.DoesNotContain(RatStudied, atThree);

        await WinFightsAsync(harness, MonsterCatalog.GiantRat, 7);
        Assert.Contains(RatStudied, await UnlockedKeysAsync(harness));
    }

    /// <summary>
    /// The claim that there is no such thing as unlocking twice, asserted rather than argued.
    /// </summary>
    [Fact]
    public async Task A_fragment_cannot_be_unlocked_twice()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        await WinFightsAsync(harness, MonsterCatalog.GiantRat, 3);

        var atThreshold = await UnlockedKeysAsync(harness);
        Assert.Contains(RatKnown, atThreshold);

        // Every key appears once. A stored unlock could have grown a duplicate row here and
        // the reading code would have shown the fragment twice.
        Assert.Equal(atThreshold.Count, atThreshold.Distinct(StringComparer.Ordinal).Count());

        var countAtThreshold = (await harness.Bestiary.LoreAsync(harness.UserId, default)).Unlocked;

        // Four more kills cross the same threshold four more times and change nothing.
        await WinFightsAsync(harness, MonsterCatalog.GiantRat, 4);

        var after = await harness.Bestiary.LoreAsync(harness.UserId, default);
        var keysAfter = await UnlockedKeysAsync(harness);

        Assert.Equal(countAtThreshold, after.Unlocked);
        Assert.Equal(atThreshold, keysAfter);
        Assert.Equal(keysAfter.Count, keysAfter.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(keysAfter.Count, after.Unlocked);
    }

    [Fact]
    public async Task The_unlocked_count_can_never_exceed_the_total()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        await WinFightsAsync(harness, MonsterCatalog.GiantRat, 12);

        var collection = await harness.Bestiary.LoreAsync(harness.UserId, default);

        Assert.InRange(collection.Unlocked, 1, collection.Total);
        Assert.Equal(
            collection.Unlocked,
            collection.Places.Sum(p => p.Fragments.Count(f => f.IsUnlocked)));
    }

    [Fact]
    public async Task One_monster_never_opens_another_monsters_fragment()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        await WinFightsAsync(harness, MonsterCatalog.Goblin, 3);

        var unlocked = await UnlockedKeysAsync(harness);

        Assert.DoesNotContain(RatSighted, unlocked);
        Assert.DoesNotContain(RatKnown, unlocked);
    }

    // -------------------------------------------------------------------------
    // The other two triggers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Place fragments hang off level and claimed quests, so the map opens for a player who
    /// progresses rather than only for one who grinds fights.
    /// </summary>
    [Fact]
    public async Task A_level_fragment_opens_when_the_level_is_reached()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        Assert.DoesNotContain(TavernLongTable, await UnlockedKeysAsync(harness));

        // Real work, which is the only thing that moves a level (DEC-003).
        var evaluator = new TodoApp.Api.Services.AchievementEvaluator();
        var gamification = new TodoApp.Api.Services.GamificationService(
            harness.Db, evaluator, harness.Quests, new ChronicleService(harness.Db));

        for (var i = 0; i < 6; i++)
        {
            var task = new TodoTask
            {
                UserId = harness.UserId, Title = $"Real work {i}", Difficulty = Difficulty.Epic
            };
            harness.Db.Tasks.Add(task);
            await harness.Db.SaveChangesAsync();

            await gamification.CompleteAsync(harness.UserId, task.Id, 0, default);
        }

        Assert.Contains(TavernLongTable, await UnlockedKeysAsync(harness));
    }

    [Fact]
    public async Task A_quest_fragment_opens_on_the_claim_and_not_on_the_completion()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var evaluator = new TodoApp.Api.Services.AchievementEvaluator();
        var gamification = new TodoApp.Api.Services.GamificationService(
            harness.Db, evaluator, harness.Quests, new ChronicleService(harness.Db));

        // Honest Work wants five tasks finished. Easy ones, so the level stays put and the
        // only thing under test is the claim.
        for (var i = 0; i < 5; i++)
        {
            var task = new TodoTask
            {
                UserId = harness.UserId, Title = $"Chore {i}", Difficulty = Difficulty.Easy
            };
            harness.Db.Tasks.Add(task);
            await harness.Db.SaveChangesAsync();

            await gamification.CompleteAsync(harness.UserId, task.Id, 0, default);
        }

        var quests = await harness.Quests.ListAsync(harness.UserId, default);
        Assert.True(quests.Single(q => q.Key == QuestCatalog.HonestWork).IsComplete);

        // Complete but unclaimed. The fragment waits for the reward to be taken.
        Assert.DoesNotContain(TavernSlate, await UnlockedKeysAsync(harness));

        var claim = await harness.Quests.ClaimAsync(harness.UserId, QuestCatalog.HonestWork, default);
        Assert.True(claim.Ok);

        Assert.Contains(TavernSlate, await UnlockedKeysAsync(harness));
    }

    // -------------------------------------------------------------------------
    // What a locked fragment is allowed to say
    // -------------------------------------------------------------------------

    /// <summary>
    /// Every fragment appears exactly once, under the place it belongs to, whether it is open
    /// or not. A collection that hid its locked rows would be a list of what the player has
    /// rather than a list of what there is.
    /// </summary>
    [Fact]
    public async Task Every_fragment_appears_exactly_once_under_its_own_place()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        await WinFightsAsync(harness, MonsterCatalog.GiantRat, 3);

        var collection = await harness.Bestiary.LoreAsync(harness.UserId, default);

        Assert.All(collection.Places, place =>
        {
            Assert.Equal(LoreCatalog.ForPlace(place.Place.Key).Count, place.Fragments.Count);
            Assert.All(place.Fragments, f => Assert.Equal(place.Place.Key, f.Fragment.PlaceKey));
        });

        var keys = collection.Places.SelectMany(p => p.Fragments).Select(f => f.Fragment.Key).ToList();

        Assert.Equal(LoreCatalog.All.Count, keys.Count);
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(collection.Total, keys.Count);
    }

    /// <summary>
    /// The service and the fragment's own predicate have to agree, or the collection would be
    /// reporting something other than the rule the catalog states.
    /// </summary>
    [Fact]
    public async Task The_collection_agrees_with_the_rule_each_fragment_states()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        await WinFightsAsync(harness, MonsterCatalog.GiantRat, 3);
        await FleeFightsAsync(harness, MonsterCatalog.Goblin, 1);

        var state = new LoreState(
            1,
            new Dictionary<string, (int Seen, int Slain)>(StringComparer.Ordinal)
            {
                [MonsterCatalog.GiantRat] = (3, 3),
                [MonsterCatalog.Goblin] = (1, 0)
            },
            new HashSet<string>(StringComparer.Ordinal));

        var collection = await harness.Bestiary.LoreAsync(harness.UserId, default);

        Assert.All(
            collection.Places.SelectMany(p => p.Fragments),
            f => Assert.Equal(f.Fragment.IsUnlockedBy(state), f.IsUnlocked));
    }

    // -------------------------------------------------------------------------
    // DEC-012
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Unlocking_lore_never_moves_experience_or_level()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var before = await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId);

        await WinFightsAsync(harness, MonsterCatalog.GiantRat, 10);

        var unlocked = await UnlockedKeysAsync(harness);
        Assert.Contains(RatStudied, unlocked);

        var after = await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId);

        Assert.Equal(before.TotalXp, after.TotalXp);
    }
}
