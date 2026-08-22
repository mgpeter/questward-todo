using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Services.Rpg;
using TodoApp.Data;
using TodoApp.Models;
using TodoApp.Models.Dice;
using TodoApp.Models.Progression;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

/// <summary>
/// The chronicle counts sightings and kills. It is the one place in the RPG layer that stores
/// a counter rather than deriving one, so what it stores has to be exactly right.
/// </summary>
/// <remarks>
/// The distinction the whole table rests on is that <c>Encounters</c> counts fights begun and
/// <c>Kills</c> counts fights won. Getting that backwards is invisible until a player who has
/// lost ten times reads that they have slain ten, so a loss and a flight are asserted as
/// explicitly as a win is.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class BestiaryTests(PostgresFixture postgres)
{
    private static SequenceDiceRoller AlwaysHits() =>
        new(Enumerable.Repeat(20, 400).ToArray());

    private sealed record Harness(
        TodoDbContext Db,
        CombatService Combat,
        BestiaryService Bestiary,
        QuestService Quests,
        Guid UserId);

    private async Task<Harness> ArrangeAsync(IDiceRoller roller, string classKey = ClassCatalog.Fighter)
    {
        await postgres.ResetAsync();
        var user = await postgres.CreateUserAsync("test|hero");

        var db = postgres.CreateContext();
        var sheets = new CharacterSheetService(db);
        var loot = new LootService(db, roller);
        var quests = new QuestService(db, loot, new ChronicleService(db));
        var adventurer = new AdventurerService(db, sheets, loot);
        var combat = new CombatService(db, roller, sheets, loot, quests, new ChronicleService(db));

        await adventurer.ChooseClassAsync(user.Id, classKey, TestContext.Current.CancellationToken);

        var character = await db.Characters.SingleAsync(c => c.UserId == user.Id);
        character.Stamina = 40;
        await db.SaveChangesAsync();

        return new Harness(db, combat, new BestiaryService(db), quests, user.Id);
    }

    /// <summary>
    /// Read on a fresh context on purpose. A tracked instance would pass even if nothing had
    /// been committed, and committing with the fight is the whole point of where the hooks sit.
    /// </summary>
    private async Task<BestiaryEntry?> StoredAsync(Guid userId, string monsterKey)
    {
        await using var db = postgres.CreateContext();

        return await db.BestiaryEntries.AsNoTracking()
            .SingleOrDefaultAsync(b => b.UserId == userId && b.MonsterKey == monsterKey);
    }

    private async Task<RpgResult<AttackOutcome>> FightToTheEndAsync(Harness harness, Guid encounterId)
    {
        var last = default(RpgResult<AttackOutcome>);

        for (var round = 0; round < 30; round++)
        {
            last = await harness.Combat.AttackAsync(harness.UserId, encounterId, default);

            if (!last.Ok || last.Value!.Encounter.IsOver)
            {
                break;
            }
        }

        return last;
    }

    // -------------------------------------------------------------------------
    // Sightings
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Starting_a_fight_records_the_sighting_before_the_first_round()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);
        Assert.True(start.Ok);

        var entry = await StoredAsync(harness.UserId, MonsterCatalog.Goblin);

        Assert.NotNull(entry);
        Assert.Equal(1, entry.Encounters);
        Assert.Equal(0, entry.Kills);
        Assert.False(entry.IsSlain);
        Assert.Equal(0, entry.BestRound);
        Assert.Equal(0, entry.GoldTaken);
    }

    [Fact]
    public async Task A_monster_never_met_has_no_row_at_all()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);

        // Absent rather than zeroed, so the codex can tell "never met" from "met and survived".
        Assert.Null(await StoredAsync(harness.UserId, MonsterCatalog.GiantRat));
    }

    /// <summary>
    /// A refused fight is not a sighting. The hook sits below every gate for this reason, and
    /// a monster out of range is the gate that is easiest to reach on purpose.
    /// </summary>
    [Fact]
    public async Task A_fight_that_was_refused_is_not_a_sighting()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var refused = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.YoungDragon, default);

        Assert.False(refused.Ok);
        Assert.Equal(RpgFailure.MonsterOutOfRange, refused.Failure);
        Assert.Null(await StoredAsync(harness.UserId, MonsterCatalog.YoungDragon));
    }

    [Fact]
    public async Task A_fight_refused_for_want_of_stamina_is_not_a_sighting()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);
        character.Stamina = 0;
        await harness.Db.SaveChangesAsync();

        var refused = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);

        Assert.False(refused.Ok);
        Assert.Null(await StoredAsync(harness.UserId, MonsterCatalog.Goblin));
    }

    // -------------------------------------------------------------------------
    // Kills, and the three endings
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Winning_records_a_kill_with_the_gold_it_paid_and_the_round_it_took()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);
        var final = await FightToTheEndAsync(harness, start.Value!.Id);

        Assert.Equal(EncounterStatus.Won, final.Value!.Encounter.Status);

        var entry = await StoredAsync(harness.UserId, MonsterCatalog.Goblin);

        Assert.NotNull(entry);
        Assert.Equal(1, entry.Encounters);
        Assert.Equal(1, entry.Kills);
        Assert.True(entry.IsSlain);
        Assert.Equal(final.Value.GoldAwarded, entry.GoldTaken);
        Assert.Equal(final.Value.Encounter.Round, entry.BestRound);
    }

    [Fact]
    public async Task Losing_counts_as_a_sighting_and_never_as_a_kill()
    {
        // The player fumbles, the goblin lands its answer, and the answer is fatal because
        // the character is standing on one hit point when it arrives.
        var harness = await ArrangeAsync(new SequenceDiceRoller(1, 15, 4));

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);

        var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);
        character.CurrentHitPoints = 1;
        character.HitPointsUpdatedAt = DateTimeOffset.UtcNow;
        await harness.Db.SaveChangesAsync();

        var final = await FightToTheEndAsync(harness, start.Value!.Id);

        Assert.Equal(EncounterStatus.Lost, final.Value!.Encounter.Status);

        var entry = await StoredAsync(harness.UserId, MonsterCatalog.Goblin);

        Assert.NotNull(entry);
        Assert.Equal(1, entry.Encounters);
        Assert.Equal(0, entry.Kills);
        Assert.False(entry.IsSlain);
        Assert.Equal(0, entry.BestRound);
        Assert.Equal(0, entry.GoldTaken);
    }

    [Fact]
    public async Task Fleeing_counts_as_a_sighting_and_never_as_a_kill()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);
        var fled = await harness.Combat.FleeAsync(harness.UserId, start.Value!.Id, default);

        Assert.Equal(EncounterStatus.Fled, fled.Value!.Status);

        var entry = await StoredAsync(harness.UserId, MonsterCatalog.Goblin);

        Assert.NotNull(entry);
        Assert.Equal(1, entry.Encounters);
        Assert.Equal(0, entry.Kills);
        Assert.Equal(0, entry.BestRound);
    }

    // -------------------------------------------------------------------------
    // Counters over several fights
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Counters_accumulate_across_several_fights()
    {
        var harness = await ArrangeAsync(AlwaysHits());
        var goldExpected = 0;

        for (var fight = 0; fight < 3; fight++)
        {
            var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);
            var final = await FightToTheEndAsync(harness, start.Value!.Id);

            Assert.Equal(EncounterStatus.Won, final.Value!.Encounter.Status);
            goldExpected += final.Value.GoldAwarded;
        }

        // A fourth fight ends in a withdrawal, so the sighting count pulls ahead of the kills.
        var fourth = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);
        await harness.Combat.FleeAsync(harness.UserId, fourth.Value!.Id, default);

        var entry = await StoredAsync(harness.UserId, MonsterCatalog.Goblin);

        Assert.NotNull(entry);
        Assert.Equal(4, entry.Encounters);
        Assert.Equal(3, entry.Kills);
        Assert.Equal(goldExpected, entry.GoldTaken);

        // One row, not four. The unique index is what makes the counters counters.
        await using var db = postgres.CreateContext();
        Assert.Equal(
            1,
            await db.BestiaryEntries.CountAsync(
                b => b.UserId == harness.UserId && b.MonsterKey == MonsterCatalog.Goblin));
    }

    /// <summary>
    /// Zero is the never-killed sentinel rather than a real round, so the first kill has to
    /// take the round outright and later kills only lower it. Both directions are exercised:
    /// a slower kill after a fast one must not overwrite the record.
    /// </summary>
    [Fact]
    public async Task Best_round_keeps_the_fewest_rounds_to_a_kill()
    {
        // Two rounds of mutual misses, then a hit that kills, then gold and a failed drop.
        // Repeated three times: slow, fast, slow.
        var harness = await ArrangeAsync(new SequenceDiceRoller(
            1, 1, 1, 1, 10, 8, 1, 100,
            10, 8, 1, 100,
            1, 1, 1, 1, 10, 8, 1, 100));

        var slow = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);
        await FightToTheEndAsync(harness, slow.Value!.Id);
        Assert.Equal(3, (await StoredAsync(harness.UserId, MonsterCatalog.GiantRat))!.BestRound);

        var fast = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);
        await FightToTheEndAsync(harness, fast.Value!.Id);
        Assert.Equal(1, (await StoredAsync(harness.UserId, MonsterCatalog.GiantRat))!.BestRound);

        var slowAgain = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);
        await FightToTheEndAsync(harness, slowAgain.Value!.Id);

        var entry = await StoredAsync(harness.UserId, MonsterCatalog.GiantRat);

        Assert.Equal(1, entry!.BestRound);
        Assert.Equal(3, entry.Kills);
    }

    [Fact]
    public async Task Two_kinds_of_monster_keep_two_separate_rows()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var rat = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);
        await FightToTheEndAsync(harness, rat.Value!.Id);

        var goblin = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);
        await harness.Combat.FleeAsync(harness.UserId, goblin.Value!.Id, default);

        Assert.True((await StoredAsync(harness.UserId, MonsterCatalog.GiantRat))!.IsSlain);
        Assert.False((await StoredAsync(harness.UserId, MonsterCatalog.Goblin))!.IsSlain);
    }

    [Fact]
    public async Task First_seen_is_the_first_sighting_and_last_seen_moves_with_the_latest()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var first = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);
        await harness.Combat.FleeAsync(harness.UserId, first.Value!.Id, default);

        var afterFirst = (await StoredAsync(harness.UserId, MonsterCatalog.Goblin))!;

        var second = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);
        await harness.Combat.FleeAsync(harness.UserId, second.Value!.Id, default);

        var afterSecond = (await StoredAsync(harness.UserId, MonsterCatalog.Goblin))!;

        Assert.Equal(afterFirst.FirstSeenAt, afterSecond.FirstSeenAt);
        Assert.True(afterSecond.LastSeenAt >= afterFirst.LastSeenAt);
    }

    // -------------------------------------------------------------------------
    // The codex
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_codex_shows_the_whole_catalog_and_counts_what_has_been_met()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var rat = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);
        await FightToTheEndAsync(harness, rat.Value!.Id);

        var goblin = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);
        await harness.Combat.FleeAsync(harness.UserId, goblin.Value!.Id, default);

        var codex = await harness.Bestiary.CodexAsync(harness.UserId, default);

        // Undiscovered monsters are listed too. A codex that only showed what was already
        // found would have nothing to aim at.
        Assert.Equal(MonsterCatalog.All.Count, codex.Total);
        Assert.Equal(MonsterCatalog.All.Count, codex.Rows.Count);
        Assert.Equal(2, codex.Discovered);
        Assert.Equal(1, codex.Slain);

        Assert.Null(codex.Rows.Single(r => r.Monster.Key == MonsterCatalog.Skeleton).Entry);

        // Ordered by level then name, so the page reads as a ladder.
        Assert.Equal(
            codex.Rows.OrderBy(r => r.Monster.Level).ThenBy(r => r.Monster.Name, StringComparer.Ordinal).ToList(),
            codex.Rows);
    }

    // -------------------------------------------------------------------------
    // DEC-012
    // -------------------------------------------------------------------------

    /// <summary>
    /// The chronicle records what happened. It does not pay for it.
    /// </summary>
    [Fact]
    public async Task Recording_a_sighting_or_a_kill_never_moves_experience_or_level()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var before = await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId);

        foreach (var key in new[] { MonsterCatalog.GiantRat, MonsterCatalog.Goblin, MonsterCatalog.Skeleton })
        {
            var start = await harness.Combat.StartAsync(harness.UserId, key, default);
            await FightToTheEndAsync(harness, start.Value!.Id);
        }

        var after = await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId);

        Assert.Equal(3, await CountEntriesAsync(harness.UserId));
        Assert.Equal(before.TotalXp, after.TotalXp);
        Assert.Equal(before.TasksCompleted, after.TasksCompleted);
    }

    private async Task<int> CountEntriesAsync(Guid userId)
    {
        await using var db = postgres.CreateContext();

        return await db.BestiaryEntries.CountAsync(b => b.UserId == userId);
    }

    // -------------------------------------------------------------------------
    // Discovery quests (DEC-014)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Discovery counts kinds, not fights, and it counts them on the first sighting whatever
    /// that fight went on to become.
    /// </summary>
    [Fact]
    public async Task Discovering_a_kind_advances_the_discovery_quest_exactly_once()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        // Met and fled from: still discovered.
        var first = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);
        await harness.Combat.FleeAsync(harness.UserId, first.Value!.Id, default);

        Assert.Equal(1, await DiscoveryCountAsync(harness));

        // The same kind again must not count twice, or the quest would measure persistence
        // rather than variety.
        var again = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);
        await harness.Combat.FleeAsync(harness.UserId, again.Value!.Id, default);

        Assert.Equal(1, await DiscoveryCountAsync(harness));

        var goblin = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);
        await FightToTheEndAsync(harness, goblin.Value!.Id);

        Assert.Equal(2, await DiscoveryCountAsync(harness));
    }

    [Fact]
    public async Task Three_kinds_discovered_completes_Field_Notes()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        foreach (var key in new[] { MonsterCatalog.GiantRat, MonsterCatalog.Goblin, MonsterCatalog.Skeleton })
        {
            var start = await harness.Combat.StartAsync(harness.UserId, key, default);
            await harness.Combat.FleeAsync(harness.UserId, start.Value!.Id, default);
        }

        var quests = await harness.Quests.ListAsync(harness.UserId, default);
        var fieldNotes = quests.Single(q => q.Key == QuestCatalog.FieldNotes);

        Assert.True(fieldNotes.IsComplete);

        // Fled from every one of them, so nothing was defeated and no gold was earned. The
        // quest completed on discovery alone.
        Assert.Equal(0, (await StoredAsync(harness.UserId, MonsterCatalog.Goblin))!.Kills);
    }

    /// <summary>
    /// The gate that keeps the game a sink for real work. A subtask pays nothing at all, and
    /// discovery is not a side door around that.
    /// </summary>
    [Fact]
    public async Task Completing_a_subtask_pays_no_discovery_progress()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var evaluator = new TodoApp.Api.Services.AchievementEvaluator();
        var gamification = new TodoApp.Api.Services.GamificationService(
            harness.Db, evaluator, harness.Quests, new ChronicleService(harness.Db));

        var parent = new TodoTask
        {
            UserId = harness.UserId, Title = "Real work", Difficulty = Difficulty.Hard
        };
        harness.Db.Tasks.Add(parent);
        await harness.Db.SaveChangesAsync();

        var subtask = new TodoTask
        {
            UserId = harness.UserId,
            ParentId = parent.Id,
            Title = "A step along the way",
            Difficulty = Difficulty.Epic
        };
        harness.Db.Tasks.Add(subtask);
        await harness.Db.SaveChangesAsync();

        await gamification.CompleteAsync(harness.UserId, subtask.Id, 0, default);

        Assert.Equal(0, await DiscoveryCountAsync(harness));
        Assert.Equal(0, await CountEntriesAsync(harness.UserId));
    }

    /// <summary>
    /// The stronger version of the same point: even a task that does pay, with all its XP,
    /// stamina, badges and quest progress, pays nothing toward discovery. Only a fight can.
    /// </summary>
    [Fact]
    public async Task Completing_a_real_task_pays_no_discovery_progress_either()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var evaluator = new TodoApp.Api.Services.AchievementEvaluator();
        var gamification = new TodoApp.Api.Services.GamificationService(
            harness.Db, evaluator, harness.Quests, new ChronicleService(harness.Db));

        var task = new TodoTask
        {
            UserId = harness.UserId, Title = "Real work", Difficulty = Difficulty.Easy
        };
        harness.Db.Tasks.Add(task);
        await harness.Db.SaveChangesAsync();

        var result = await gamification.CompleteAsync(harness.UserId, task.Id, 0, default);

        Assert.Equal(Difficulty.Easy.BaseXp(), result!.XpGained);
        Assert.Equal(0, await DiscoveryCountAsync(harness));
        Assert.Equal(0, await CountEntriesAsync(harness.UserId));
    }

    // -------------------------------------------------------------------------
    // Discovery progress is derived from these rows, not counted up beside them
    // -------------------------------------------------------------------------

    /// <summary>
    /// A kind is met for the first time exactly once, and the discovery quests unlock late.
    /// Progress that only accrued while the quest was already unlocked threw those sightings
    /// away, and the availability band means they can never be made again: from level 8 the
    /// tavern offers nothing below monster level 4 ever again. Full Catalogue would have sat
    /// at zero of twelve with a ceiling of ten, permanently.
    /// </summary>
    [Fact]
    public async Task Kinds_met_before_a_quest_unlocks_still_count_toward_it()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var early = new[]
        {
            MonsterCatalog.GiantRat,
            MonsterCatalog.Goblin,
            MonsterCatalog.Skeleton,
            MonsterCatalog.CarrionCrows
        };

        foreach (var key in early)
        {
            var start = await harness.Combat.StartAsync(harness.UserId, key, default);
            Assert.True(start.Ok);
            await harness.Combat.FleeAsync(harness.UserId, start.Value!.Id, default);
        }

        await ReachLevelAsync(harness, 8);

        var full = (await harness.Quests.ListAsync(harness.UserId, default))
            .Single(q => q.Key == QuestCatalog.FullCatalogue);

        Assert.False(full.IsLocked);
        Assert.Equal(early.Length, full.Objectives.Single().Current);

        // And none of those four is on the board any more: the band has moved past them, so
        // a count that started at zero here could never have got them back.
        Assert.All(early, key => Assert.DoesNotContain(
            key, MonsterCatalog.AvailableAt(8).Select(m => m.Key)));
    }

    /// <summary>
    /// The end of the ladder, played out rather than reasoned about. Full Catalogue wants
    /// twelve kinds at level 8 and Long Service eighteen at level 14, and both are only
    /// reachable because a kind met at level 2 still counts.
    /// </summary>
    [Fact]
    public async Task A_career_that_fights_at_every_level_finishes_the_late_discovery_quests()
    {
        var harness = await ArrangeAsync(AlwaysHits());
        var met = new HashSet<string>(StringComparer.Ordinal);

        for (var level = 1; level <= MonsterCatalog.TopLevel; level++)
        {
            await ReachLevelAsync(harness, level);

            var reached = LevelCurve.LevelForXp(
                (await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId)).TotalXp);

            foreach (var monster in MonsterCatalog.AvailableAt(reached))
            {
                if (!met.Add(monster.Key))
                {
                    continue;
                }

                // Met and fled from. Discovery counts a fight begun, so none of this needs
                // to be won, and nothing here pays experience.
                var start = await harness.Combat.StartAsync(harness.UserId, monster.Key, default);
                Assert.True(start.Ok, $"{monster.Key} was refused at level {reached}");
                await harness.Combat.FleeAsync(harness.UserId, start.Value!.Id, default);
            }
        }

        Assert.Equal(MonsterCatalog.All.Count, met.Count);

        var quests = await harness.Quests.ListAsync(harness.UserId, default);
        var full = quests.Single(q => q.Key == QuestCatalog.FullCatalogue);

        Assert.True(full.IsComplete, $"Full Catalogue reached {full.Objectives.Single().Current}");

        // Long Service also asks for 5000 gold, which fleeing every fight does not pay, so
        // only the discovery half of it is the claim under test here.
        var seen = quests.Single(q => q.Key == QuestCatalog.LongService)
            .Objectives.Single(o => o.Id == "seen");

        Assert.True(seen.IsComplete, $"Long Service saw {seen.Current} of {seen.Required}");
    }

    /// <summary>
    /// The AddBestiary migration seeds a row for every monster a Phase 3 player had already
    /// fought, and seeds no quest counters to go with them. A stored counter would have made
    /// every one of those kinds a repeat sighting from then on, so a player who had fought
    /// all nineteen would have been locked out of all four discovery quests by the migration
    /// that was written to reward them.
    /// </summary>
    [Fact]
    public async Task A_backfilled_bestiary_row_counts_as_a_kind_already_discovered()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var seeded = new[] { MonsterCatalog.GiantRat, MonsterCatalog.Goblin, MonsterCatalog.Skeleton };

        foreach (var key in seeded)
        {
            harness.Db.BestiaryEntries.Add(new BestiaryEntry
            {
                UserId = harness.UserId,
                MonsterKey = key,
                Encounters = 4,
                Kills = 2,
                GoldTaken = 30,
                BestRound = 2
            });
        }

        await harness.Db.SaveChangesAsync();

        // Exactly what the migration leaves behind: rows, and no quest progress at all.
        Assert.Empty(await harness.Db.QuestProgress
            .Where(p => p.UserId == harness.UserId)
            .ToListAsync(TestContext.Current.CancellationToken));

        Assert.Equal(seeded.Length, await DiscoveryCountAsync(harness));

        var claim = await harness.Quests.ClaimAsync(harness.UserId, QuestCatalog.FieldNotes, default);

        Assert.True(claim.Ok);
        Assert.Equal(ItemCatalog.CartographersLens, claim.Value!.Item!.ItemKey);
    }

    /// <summary>
    /// Derived progress does not wait for the quest to unlock, so a low level character can
    /// now satisfy a higher level quest's objectives. The board still draws it as locked, and
    /// claiming has to agree with the board.
    /// </summary>
    [Fact]
    public async Task A_quest_the_character_is_too_low_for_cannot_be_claimed_early()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        // Level 2 is the first level the band reaches six kinds, which is what Strange
        // Company asks for. It does not open until level 3.
        await ReachLevelAsync(harness, 2);

        foreach (var monster in MonsterCatalog.AvailableAt(2))
        {
            var start = await harness.Combat.StartAsync(harness.UserId, monster.Key, default);
            Assert.True(start.Ok);
            await harness.Combat.FleeAsync(harness.UserId, start.Value!.Id, default);
        }

        var locked = (await harness.Quests.ListAsync(harness.UserId, default))
            .Single(q => q.Key == QuestCatalog.StrangeCompany);

        Assert.True(locked.IsComplete);
        Assert.True(locked.IsLocked);

        var early = await harness.Quests.ClaimAsync(harness.UserId, QuestCatalog.StrangeCompany, default);

        Assert.False(early.Ok);
        Assert.Equal(RpgFailure.QuestNotComplete, early.Failure);
        Assert.Contains("level 3", early.Message);

        await ReachLevelAsync(harness, 3);

        var claim = await harness.Quests.ClaimAsync(harness.UserId, QuestCatalog.StrangeCompany, default);

        Assert.True(claim.Ok);
        Assert.Equal(ItemCatalog.HeartwoodToken, claim.Value!.Item!.ItemKey);
    }

    private async Task<int> DiscoveryCountAsync(Harness harness)
    {
        var quests = await harness.Quests.ListAsync(harness.UserId, default);

        return quests.Single(q => q.Key == QuestCatalog.FieldNotes).Objectives.Single().Current;
    }

    /// <summary>
    /// Raises the character to a level the only way anything is allowed to, by finishing real
    /// work. Nothing in the RPG layer may pay experience (DEC-012), and a test that reached in
    /// and assigned TotalXp would be the first thing in the repository to write it.
    /// </summary>
    private async Task ReachLevelAsync(Harness harness, int level)
    {
        var gamification = new TodoApp.Api.Services.GamificationService(
            harness.Db,
            new TodoApp.Api.Services.AchievementEvaluator(),
            harness.Quests,
            new ChronicleService(harness.Db));

        while (true)
        {
            var character = await harness.Db.Characters
                .SingleAsync(c => c.UserId == harness.UserId);

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
}
