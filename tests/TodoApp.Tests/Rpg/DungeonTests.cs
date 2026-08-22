using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Mapping;
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
/// What a dungeon is, before any of it reaches a database or a die.
/// </summary>
public class DungeonRuleTests
{
    [Fact]
    public void Every_dungeon_is_reachable_content_all_the_way_down()
    {
        Assert.NotEmpty(DungeonCatalog.All);

        Assert.Equal(
            DungeonCatalog.All.Count,
            DungeonCatalog.All.Select(d => d.Key).Distinct(StringComparer.Ordinal).Count());

        foreach (var dungeon in DungeonCatalog.All)
        {
            // Two rooms is the smallest thing that is a run rather than a fight: one drawn from
            // the pool and the boss.
            Assert.True(dungeon.Rooms >= 2, $"{dungeon.Key} is not long enough to be a run.");
            Assert.True(dungeon.ClearGold > 0, $"{dungeon.Key} pays nothing for being cleared.");

            Assert.NotEmpty(dungeon.Pool);
            Assert.All(dungeon.Pool, room =>
            {
                Assert.True(MonsterCatalog.Exists(room.MonsterKey), $"{room.MonsterKey} is not a monster.");
                Assert.True(room.Weight > 0, $"{room.MonsterKey} can never be drawn.");
            });

            Assert.NotNull(dungeon.Boss);

            // A reward that could roll nothing would make the last room of a five room run
            // indistinguishable from the first.
            Assert.NotEmpty(dungeon.RewardTable);
            Assert.All(dungeon.RewardTable, entry =>
            {
                Assert.NotNull(ItemCatalog.Find(entry.ItemKey));
                Assert.True(entry.Weight > 0, $"{entry.ItemKey} can never be drawn.");
            });
        }
    }

    /// <summary>
    /// The named guard for the level rule, which is deliberately not the tavern's.
    /// </summary>
    /// <remarks>
    /// Monsters use an asymmetric band so an unwinnable opponent never reaches the board. Read
    /// against a dungeon's gate that band would retire the Sunken Warren at level five and the
    /// Barrow Deeps at ten, and with three dungeons two bands apart a level five and a level ten
    /// character would have no dungeon at all. This asserts the hole is not there.
    /// </remarks>
    [Fact]
    public void A_dungeon_unlocks_at_its_level_and_is_never_retired()
    {
        var warren = DungeonCatalog.Find(DungeonCatalog.SunkenWarren)!;

        Assert.False(warren.IsAvailableAt(1));
        Assert.True(warren.IsAvailableAt(2));
        Assert.True(warren.IsAvailableAt(5));
        Assert.True(warren.IsAvailableAt(14));

        Assert.Empty(DungeonCatalog.AvailableAt(1));
        Assert.Equal(3, DungeonCatalog.AvailableAt(12).Count);

        // No level from the first unlock upwards has nothing to run.
        for (var level = 2; level <= MonsterCatalog.TopLevel; level++)
        {
            Assert.NotEmpty(DungeonCatalog.AvailableAt(level));
        }
    }

    /// <summary>
    /// The fact the room path's missing band check rests on, asserted rather than assumed.
    /// </summary>
    /// <remarks>
    /// Every boss deliberately sits above the door it is behind, and the tavern's band would
    /// therefore refuse it to a character who has only just unlocked the dungeon. If a retuning
    /// ever brought a boss back inside the band, skipping the check would stop being load-bearing
    /// and this test is where that shows up.
    /// </remarks>
    [Fact]
    public void A_boss_is_deliberately_out_of_the_band_at_the_level_that_unlocks_its_dungeon()
    {
        foreach (var dungeon in DungeonCatalog.All)
        {
            var boss = dungeon.Boss!;

            Assert.True(
                boss.Level > dungeon.Level,
                $"{dungeon.Key}'s boss is not a step up from its own door.");

            Assert.False(
                boss.IsAvailableAt(dungeon.Level),
                $"{boss.Key} is inside the tavern's band at level {dungeon.Level}.");
        }
    }

    /// <summary>Everything the pool draws from must be fightable by whoever got through the door.</summary>
    [Fact]
    public void A_pooled_room_is_never_deeper_than_the_boss_it_leads_to()
    {
        foreach (var dungeon in DungeonCatalog.All)
        {
            Assert.All(dungeon.Pool, room =>
            {
                var monster = MonsterCatalog.Find(room.MonsterKey)!;

                Assert.True(
                    monster.Level <= dungeon.Boss!.Level,
                    $"{room.MonsterKey} is worse than {dungeon.Key}'s own boss.");
            });
        }
    }

    [Fact]
    public void The_rolled_chain_round_trips_and_a_corrupt_one_reads_as_empty()
    {
        var run = new DungeonRun { DungeonKey = DungeonCatalog.SunkenWarren };

        DungeonRuns.Write(run, [MonsterCatalog.GiantRat, MonsterCatalog.Goblin, MonsterCatalog.HedgeTroll]);

        Assert.Equal(
            [MonsterCatalog.GiantRat, MonsterCatalog.Goblin, MonsterCatalog.HedgeTroll],
            DungeonRuns.Read(run));

        // A corrupt blob must leave the run abandonable rather than throwing on every read of it.
        run.Rooms = "{not json";
        Assert.Empty(DungeonRuns.Read(run));

        run.Rooms = string.Empty;
        Assert.Empty(DungeonRuns.Read(run));
    }

    /// <summary>
    /// Depth is derived, and this is the guard that keeps it that way.
    /// </summary>
    /// <remarks>
    /// A stored counter is one missed increment away from claiming a run is deeper than the rooms
    /// it has actually won, and there would be no way to tell which of the two was lying. The
    /// count of won encounters cannot disagree with the encounters.
    /// </remarks>
    [Fact]
    public void The_run_row_carries_no_depth_of_its_own()
    {
        Assert.Null(typeof(DungeonRun).GetProperty("Depth"));
        Assert.Null(typeof(DungeonRun).GetProperty("RoomsCleared"));
    }

    [Fact]
    public void The_wire_marks_the_room_the_player_is_standing_in()
    {
        var run = new DungeonRun { DungeonKey = DungeonCatalog.SunkenWarren };
        IReadOnlyList<string> rooms =
            [MonsterCatalog.GiantRat, MonsterCatalog.Goblin, MonsterCatalog.HedgeTroll];

        var live = new DungeonRunView(run, rooms, Depth: 1, Encounter: null).ToDto();

        Assert.Equal(["cleared", "current", "ahead"], live.Rooms.Select(r => r.State));
        Assert.Equal("Hedge Troll", live.Rooms[2].MonsterName);
        Assert.Equal("The Sunken Warren", live.Name);
        Assert.Equal("active", live.Status);
        Assert.Null(live.Encounter);

        // A run that ended has no current room, whichever way it ended. The room that beat the
        // player reads as ahead, which is the truth: it was never won.
        run.Status = DungeonRunStatus.Failed;

        var failed = new DungeonRunView(run, rooms, Depth: 1, Encounter: null).ToDto();

        Assert.Equal(["cleared", "ahead", "ahead"], failed.Rooms.Select(r => r.State));
        Assert.Equal("failed", failed.Status);
    }

    [Fact]
    public void The_wire_prices_a_run_in_the_stamina_it_actually_costs()
    {
        var warren = DungeonCatalog.Find(DungeonCatalog.SunkenWarren)!.ToDto();

        Assert.Equal(CombatService.StaminaPerEncounter, warren.StaminaPerRoom);
        Assert.Equal(3 * CombatService.StaminaPerEncounter, warren.TotalStaminaCost);
        Assert.Equal("Hedge Troll", warren.BossName);
        Assert.Equal("uncommon", warren.RewardFloor);
    }

    /// <summary>A key no longer in the catalog reads as itself rather than as an empty label.</summary>
    [Fact]
    public void A_run_of_a_retired_dungeon_still_renders()
    {
        var run = new DungeonRun { DungeonKey = "flooded-annexe" };

        var dto = new DungeonRunView(run, ["swamp-thing"], Depth: 0, Encounter: null).ToDto();

        Assert.Equal("flooded-annexe", dto.Name);
        Assert.Equal("swamp-thing", dto.Rooms[0].MonsterName);
    }
}

/// <summary>
/// Runs against the real database and the real dice seam.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class DungeonRunTests(PostgresFixture postgres)
{
    private sealed record Harness(
        TodoDbContext Db,
        CombatService Combat,
        DungeonService Dungeons,
        QuestService Quests,
        Guid UserId);

    private async Task<Harness> ArrangeAsync(IDiceRoller roller, int level = 2, int stamina = 20)
    {
        await postgres.ResetAsync();
        var user = await postgres.CreateUserAsync("test|hero");

        var db = postgres.CreateContext();
        var sheets = new CharacterSheetService(db);
        var loot = new LootService(db, roller);
        var quests = new QuestService(db, loot, new ChronicleService(db));
        var adventurer = new AdventurerService(db, sheets, loot);
        var combat = new CombatService(db, roller, sheets, loot, quests, new ChronicleService(db));

        // The same context for both, which is what production's scoped registration gives them:
        // the run a room's fight ends is the very instance this service is holding.
        var dungeons = new DungeonService(db, roller, sheets, combat);

        await adventurer.ChooseClassAsync(
            user.Id, ClassCatalog.Fighter, TestContext.Current.CancellationToken);

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

    /// <summary>Puts the fight in a room on a chosen number of hit points.</summary>
    private static async Task WoundAsync(TodoDbContext db, Encounter encounter, int hitPoints)
    {
        encounter.MonsterHitPoints = hitPoints;
        await db.SaveChangesAsync();
    }

    private static async Task<Character> CharacterAsync(Harness harness) =>
        await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId, TestContext.Current.CancellationToken);

    /// <summary>
    /// Opening a run rolls one die per room the boss does not occupy, and nothing else.
    /// </summary>
    /// <remarks>
    /// The boss is fixed by the catalog, so the last room costs no roll and cannot come out wrong
    /// however the pool is retuned. The two d12s are the Sunken Warren's pool weights summed.
    /// </remarks>
    [Fact]
    public async Task Opening_a_run_rolls_one_die_a_room_and_never_for_the_boss()
    {
        var script = new SequenceDiceRoller(1, 1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller);

        var started = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        Assert.True(started.Ok);

        var run = started.Value!;

        Assert.Equal(
            [MonsterCatalog.GiantRat, MonsterCatalog.GiantRat, MonsterCatalog.HedgeTroll],
            run.Rooms);

        Assert.Equal(0, run.Depth);
        Assert.Equal(DungeonRunStatus.Active, run.Run.Status);
        Assert.Null(run.Encounter);

        // Two draws over the summed pool weight, and not a third for the boss.
        Assert.Equal(2, script.RollCount);
        Assert.Equal([12, 12], roller.Sides);
    }

    /// <summary>
    /// Opening a run is not a fight and is not charged as one.
    /// </summary>
    /// <remarks>
    /// The whole DEC-012 pricing of a dungeon is one stamina per room charged at the door of each
    /// room. A charge here as well would price a three room run at four.
    /// </remarks>
    [Fact]
    public async Task Opening_a_run_costs_no_stamina_of_its_own()
    {
        var harness = await ArrangeAsync(new SequenceDiceRoller(1, 1), stamina: 9);

        await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        Assert.Equal(9, (await CharacterAsync(harness)).Stamina);
    }

    /// <summary>
    /// A reload reads the chain back rather than rolling a new one.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the chain is stored (DEC-002). Derived on read it would reshuffle
    /// on every request, so a reload would be a free re-roll of a room the player did not like
    /// and no two reads of the same run would agree. The script has exactly the two dice the
    /// opening draw needs, so a third read reaching for the roller throws rather than passing.
    /// </remarks>
    [Fact]
    public async Task A_reload_reads_the_chain_back_rather_than_rolling_a_new_one()
    {
        var script = new SequenceDiceRoller(1, 1);
        var harness = await ArrangeAsync(script);

        var started = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        var first = await harness.Dungeons.ActiveAsync(
            harness.UserId, TestContext.Current.CancellationToken);

        var second = await harness.Dungeons.ActiveAsync(
            harness.UserId, TestContext.Current.CancellationToken);

        Assert.Equal(started.Value!.Rooms, first!.Rooms);
        Assert.Equal(started.Value.Rooms, second!.Rooms);
        Assert.Equal(started.Value.Run.Id, second.Run.Id);

        Assert.Equal(2, script.RollCount);
    }

    /// <summary>
    /// The DEC-012 guard: a three room run costs three stamina, and clearing it pays its reward.
    /// </summary>
    /// <remarks>
    /// This is the test the whole feature is priced against. The tempting shortcut is to charge
    /// once for the run, at which point one unit of real work buys three fights, three sets of
    /// gold and three chances at a drop, which is exactly the inflation DEC-012 exists to refuse.
    /// <para>
    /// The dice, in order: two pool draws, then per room a d20 to hit, the longsword's d8, the
    /// monster's gold span and a d100 that is deliberately 100 so nothing ordinary drops. The
    /// boss's room adds the clear tail: the reward table's summed weight, the rarity d100, and
    /// one affix roll because the floor is Uncommon. Each room is wounded to a single hit point
    /// first so the fight fits in one round and the monster never answers.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_three_room_run_costs_three_stamina_and_pays_on_the_last_blow()
    {
        var script = new SequenceDiceRoller(
            1, 1,
            18, 4, 3, 100,
            18, 4, 3, 100,
            18, 4, 20, 100, 1, 1, 1);

        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller, stamina: 9);

        var warren = DungeonCatalog.Find(DungeonCatalog.SunkenWarren)!;

        var goldBefore = (await CharacterAsync(harness)).Gold;

        var started = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        var runId = started.Value!.Run.Id;
        AttackOutcome? last = null;

        for (var room = 0; room < 3; room++)
        {
            var entered = await harness.Dungeons.EnterAsync(
                harness.UserId, runId, TestContext.Current.CancellationToken);

            Assert.True(entered.Ok);
            Assert.Equal(room, entered.Value!.Depth);

            var encounter = entered.Value.Encounter!;

            Assert.Equal(runId, encounter.DungeonRunId);
            Assert.Equal(started.Value.Rooms[room], encounter.MonsterKey);

            await WoundAsync(harness.Db, encounter, 1);

            var round = await harness.Combat.AttackAsync(
                harness.UserId, encounter.Id, TestContext.Current.CancellationToken);

            Assert.True(round.Ok);
            Assert.Equal(EncounterStatus.Won, round.Value!.Encounter.Status);

            last = round.Value;
        }

        // Three rooms, three fights, three units of real work. Not one, and not four.
        Assert.Equal(6, (await CharacterAsync(harness)).Stamina);

        var run = await harness.Db.DungeonRuns.AsNoTracking()
            .SingleAsync(r => r.Id == runId, TestContext.Current.CancellationToken);

        Assert.Equal(DungeonRunStatus.Cleared, run.Status);
        Assert.NotNull(run.EndedAt);
        Assert.Equal(warren.ClearGold, run.GoldAwarded);

        // Two rats at three gold each, thirty seven off the troll, and the clear bonus on top.
        Assert.Equal(goldBefore + 3 + 3 + 37 + warren.ClearGold, (await CharacterAsync(harness)).Gold);

        // The round that killed the boss reports the clear gold as gold gained, because it was.
        Assert.Equal(37 + warren.ClearGold, last!.GoldAwarded);

        // The encounter row keeps the monster's own purse, so the chronicle and the bestiary are
        // not told a Hedge Troll was carrying the dungeon's reward.
        Assert.Equal(37, last.Encounter.GoldAwarded);

        var reward = await harness.Db.InventoryItems.AsNoTracking()
            .SingleAsync(
                i => i.UserId == harness.UserId && i.ItemKey == ItemCatalog.GoblinCleaver,
                TestContext.Current.CancellationToken);

        // The round reports the reward it just handed over. This is the case that used to go
        // silent: the Hedge Troll's own d100 came up 100 and it dropped nothing, so the response
        // carried no item at all while its quest chip on the same card counted two acquired and
        // the Goblin Cleaver was already in the bag.
        Assert.Null(last.Loot);
        Assert.Equal(reward.Id, last.ClearReward!.Id);

        // The rarity d100 rolled a 1, which is Common. The floor lifted it.
        Assert.Equal(Rarity.Uncommon, reward.Rarity);
        Assert.NotNull(reward.PrefixKey ?? reward.SuffixKey);

        var affixPool = AffixRules.EligibleFor(ItemSlot.Weapon, Rarity.Uncommon).Count;

        Assert.Equal(17, script.RollCount);
        Assert.Equal(
            [12, 12, 20, 8, 5, 100, 20, 8, 5, 100, 20, 8, 28, 100, 13, 100, affixPool],
            roller.Sides);
    }

    /// <summary>
    /// The room the dungeon walks you into is one the tavern would refuse.
    /// </summary>
    /// <remarks>
    /// The pair matters more than either half. The band is skipped for a room and kept for the
    /// tavern, so the only way to reach a Hedge Troll at level two is to have paid for the two
    /// rooms in front of it.
    /// </remarks>
    [Fact]
    public async Task The_tavern_still_refuses_the_boss_the_dungeon_walks_you_into()
    {
        var harness = await ArrangeAsync(new SequenceDiceRoller(1, 1));

        var refused = await harness.Combat.StartAsync(
            harness.UserId, MonsterCatalog.HedgeTroll, TestContext.Current.CancellationToken);

        Assert.False(refused.Ok);
        Assert.Equal(RpgFailure.MonsterOutOfRange, refused.Failure);

        var started = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        var run = started.Value!;

        // Straight to the boss's room, by winning the two in front of it in the database rather
        // than at the dice. The point here is the gate, not the fighting.
        await WinRoomsAsync(harness, run, 2);

        var entered = await harness.Dungeons.EnterAsync(
            harness.UserId, run.Run.Id, TestContext.Current.CancellationToken);

        Assert.True(entered.Ok);
        Assert.Equal(MonsterCatalog.HedgeTroll, entered.Value!.Encounter!.MonsterKey);
    }

    /// <summary>
    /// Writes won rooms straight to the database, for tests about the gates rather than the dice.
    /// </summary>
    /// <remarks>
    /// Deliberately writes real encounter rows rather than a depth counter, because there is no
    /// depth counter: depth is a count of exactly these rows (DEC-002), so this is the only way
    /// to fake it and that is the point.
    /// </remarks>
    private static async Task WinRoomsAsync(Harness harness, DungeonRunView run, int count)
    {
        for (var room = 0; room < count; room++)
        {
            harness.Db.Encounters.Add(new Encounter
            {
                UserId = harness.UserId,
                MonsterKey = run.Rooms[room],
                MonsterHitPoints = 0,
                Status = EncounterStatus.Won,
                DungeonRunId = run.Run.Id,
                EndedAt = DateTimeOffset.UtcNow
            });
        }

        await harness.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Losing a room ends the run, at the one site that already sets Lost.</summary>
    [Fact]
    public async Task A_room_lost_is_a_run_failed()
    {
        // Two pool draws, then the player fumbles and the rat rolls a natural twenty: a critical,
        // so two d4s of damage against a character standing on one hit point.
        var script = new SequenceDiceRoller(1, 1, 1, 20, 4, 4);
        var harness = await ArrangeAsync(script);

        var started = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        var entered = await harness.Dungeons.EnterAsync(
            harness.UserId, started.Value!.Run.Id, TestContext.Current.CancellationToken);

        var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);
        character.CurrentHitPoints = 1;
        character.HitPointsUpdatedAt = DateTimeOffset.UtcNow;
        await harness.Db.SaveChangesAsync();

        var round = await harness.Combat.AttackAsync(
            harness.UserId, entered.Value!.Encounter!.Id, TestContext.Current.CancellationToken);

        Assert.Equal(EncounterStatus.Lost, round.Value!.Encounter.Status);

        var run = await harness.Db.DungeonRuns.AsNoTracking()
            .SingleAsync(r => r.Id == started.Value.Run.Id, TestContext.Current.CancellationToken);

        Assert.Equal(DungeonRunStatus.Failed, run.Status);
        Assert.NotNull(run.EndedAt);

        // And the run is closed, so the next one is allowed.
        Assert.Null(await harness.Dungeons.ActiveAsync(
            harness.UserId, TestContext.Current.CancellationToken));
    }

    /// <summary>Walking out of a room walks out of the dungeon.</summary>
    [Fact]
    public async Task Fleeing_a_room_abandons_the_run()
    {
        var script = new SequenceDiceRoller(1, 1);
        var harness = await ArrangeAsync(script);

        var started = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        var entered = await harness.Dungeons.EnterAsync(
            harness.UserId, started.Value!.Run.Id, TestContext.Current.CancellationToken);

        var fled = await harness.Combat.FleeAsync(
            harness.UserId, entered.Value!.Encounter!.Id, TestContext.Current.CancellationToken);

        Assert.True(fled.Ok);
        Assert.Equal(EncounterStatus.Fled, fled.Value!.Status);

        var run = await harness.Db.DungeonRuns.AsNoTracking()
            .SingleAsync(r => r.Id == started.Value.Run.Id, TestContext.Current.CancellationToken);

        // Abandoned rather than Failed: nothing beat the player, they left.
        Assert.Equal(DungeonRunStatus.Abandoned, run.Status);

        // Fleeing rolls nothing, and neither does closing the run behind it.
        Assert.Equal(2, script.RollCount);
    }

    /// <summary>Abandoning with a fight open ends both, through the ordinary flee path.</summary>
    [Fact]
    public async Task Abandoning_a_run_releases_the_fight_it_was_holding()
    {
        var script = new SequenceDiceRoller(1, 1);
        var harness = await ArrangeAsync(script);

        var started = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        await harness.Dungeons.EnterAsync(
            harness.UserId, started.Value!.Run.Id, TestContext.Current.CancellationToken);

        var abandoned = await harness.Dungeons.AbandonAsync(
            harness.UserId, started.Value.Run.Id, TestContext.Current.CancellationToken);

        Assert.True(abandoned.Ok);
        Assert.Equal(DungeonRunStatus.Abandoned, abandoned.Value!.Run.Status);
        Assert.Null(abandoned.Value.Encounter);

        // The encounter slot is free again, so the tavern reopens.
        Assert.Null(await harness.Combat.ActiveAsync(
            harness.UserId, TestContext.Current.CancellationToken));

        var tavern = await harness.Combat.StartAsync(
            harness.UserId, MonsterCatalog.GiantRat, TestContext.Current.CancellationToken);

        Assert.True(tavern.Ok);
        Assert.Equal(2, script.RollCount);
    }

    [Fact]
    public async Task Abandoning_a_run_with_no_room_open_just_closes_it()
    {
        var script = new SequenceDiceRoller(1, 1);
        var harness = await ArrangeAsync(script);

        var started = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        var abandoned = await harness.Dungeons.AbandonAsync(
            harness.UserId, started.Value!.Run.Id, TestContext.Current.CancellationToken);

        Assert.True(abandoned.Ok);
        Assert.Equal(DungeonRunStatus.Abandoned, abandoned.Value!.Run.Status);

        var again = await harness.Dungeons.AbandonAsync(
            harness.UserId, started.Value.Run.Id, TestContext.Current.CancellationToken);

        Assert.False(again.Ok);
        Assert.Equal(RpgFailure.DungeonOver, again.Failure);
        Assert.Equal(2, script.RollCount);
    }

    /// <summary>
    /// The three refusals that keep one encounter slot and one run from stranding each other.
    /// </summary>
    [Fact]
    public async Task A_run_and_a_tavern_fight_cannot_be_open_at_the_same_time()
    {
        var script = new SequenceDiceRoller(1, 1, 1, 1);
        var harness = await ArrangeAsync(script);

        var started = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        // A second run, with no fight open at all.
        var second = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        Assert.False(second.Ok);
        Assert.Equal(RpgFailure.DungeonInProgress, second.Failure);

        // A tavern fight, which would spend the one encounter slot outside the run and strand it.
        var tavern = await harness.Combat.StartAsync(
            harness.UserId, MonsterCatalog.GiantRat, TestContext.Current.CancellationToken);

        Assert.False(tavern.Ok);
        Assert.Equal(RpgFailure.DungeonInProgress, tavern.Failure);

        await harness.Dungeons.EnterAsync(
            harness.UserId, started.Value!.Run.Id, TestContext.Current.CancellationToken);

        // A second room while the first one is still being fought.
        var again = await harness.Dungeons.EnterAsync(
            harness.UserId, started.Value.Run.Id, TestContext.Current.CancellationToken);

        Assert.False(again.Ok);
        Assert.Equal(RpgFailure.EncounterAlreadyActive, again.Failure);

        // Every refusal above was decided before anything reached the roller, so a retry sees the
        // dice the first attempt would have.
        Assert.Equal(2, script.RollCount);
    }

    /// <summary>
    /// A room with no stamina behind it is refused by the same check a tavern fight is.
    /// </summary>
    [Fact]
    public async Task A_room_with_no_stamina_left_is_refused_and_the_run_survives()
    {
        var harness = await ArrangeAsync(new SequenceDiceRoller(1, 1), stamina: 0);

        var started = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        var entered = await harness.Dungeons.EnterAsync(
            harness.UserId, started.Value!.Run.Id, TestContext.Current.CancellationToken);

        Assert.False(entered.Ok);
        Assert.Equal(RpgFailure.NotEnoughStamina, entered.Failure);

        // The run is still there and still enterable once there is work behind it, so a run is
        // never lost to an empty stamina bar.
        var run = await harness.Dungeons.ActiveAsync(
            harness.UserId, TestContext.Current.CancellationToken);

        Assert.NotNull(run);
        Assert.Equal(DungeonRunStatus.Active, run.Run.Status);
        Assert.Equal(0, run.Depth);
    }

    /// <summary>
    /// What crosses from one room to the next, and what does not.
    /// </summary>
    /// <remarks>
    /// Status effects live on the encounter, which is the ruling that made them safe to add: the
    /// fight ends, the row stops being read, and nothing has to clean up after it. A room is a
    /// new encounter, so an affliction does not follow the player through the door. Hit points do,
    /// because they live on the character and always have, and that is what makes a long run a
    /// war of attrition rather than three unrelated fights.
    /// </remarks>
    [Fact]
    public async Task Hit_points_carry_between_rooms_and_afflictions_do_not()
    {
        var script = new SequenceDiceRoller(1, 1, 18, 4, 3, 100);
        var harness = await ArrangeAsync(script);

        var started = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        var first = await harness.Dungeons.EnterAsync(
            harness.UserId, started.Value!.Run.Id, TestContext.Current.CancellationToken);

        var encounter = first.Value!.Encounter!;

        // An affliction on the board and a character walking wounded, both set directly so the
        // test is about what survives the door rather than about how either arrived.
        StatusEffects.Write(
            encounter,
            [new StatusEffect(EffectKind.Poisoned, EffectTarget.Player, 5, 2, MonsterCatalog.GiantRat)]);

        var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);
        character.CurrentHitPoints = 4;
        character.HitPointsUpdatedAt = DateTimeOffset.UtcNow;

        encounter.MonsterHitPoints = 1;
        await harness.Db.SaveChangesAsync();

        var won = await harness.Combat.AttackAsync(
            harness.UserId, encounter.Id, TestContext.Current.CancellationToken);

        // Read from the round rather than asserted as a literal, because the Fighter's Second Wind
        // heals on a win and the number that walks through the door is the one after it.
        var wounded = won.Value!.PlayerHitPoints;

        Assert.True(wounded < won.Value.PlayerMaxHitPoints, "The character healed to full, so the door proves nothing.");

        var second = await harness.Dungeons.EnterAsync(
            harness.UserId, started.Value.Run.Id, TestContext.Current.CancellationToken);

        var next = second.Value!.Encounter!;

        Assert.NotEqual(encounter.Id, next.Id);

        // The affliction stayed with the room it was applied in. Effects live on the encounter,
        // which is exactly what stops one having to be cleaned up after a fight.
        Assert.Empty(StatusEffects.Read(next));

        // The wound came through with the player. Regeneration is applied on read and is measured
        // in minutes, so nothing has healed in the time this test took.
        Assert.Equal(wounded, (await CharacterAsync(harness)).CurrentHitPoints);
    }

    /// <summary>
    /// A run whose dungeon has left the catalog still closes when its last room is won.
    /// </summary>
    /// <remarks>
    /// Whether a run is finished is a fact about the rooms that were fought; what it pays is
    /// content. Reading the two in the other order would leave a run that has won every one of its
    /// rooms stuck Active forever, holding the one encounter slot, with abandoning the only way
    /// out of a dungeon the player had actually cleared.
    /// </remarks>
    [Fact]
    public async Task A_run_of_a_retired_dungeon_still_closes_on_its_last_room()
    {
        var script = new SequenceDiceRoller(1, 1, 18, 4, 3, 100);
        var harness = await ArrangeAsync(script);

        var started = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        var run = started.Value!;

        await WinRoomsAsync(harness, run, 2);

        var entered = await harness.Dungeons.EnterAsync(
            harness.UserId, run.Run.Id, TestContext.Current.CancellationToken);

        // Retired between opening the last room and winning it, which is what a catalog edit
        // shipped mid-fight looks like from the row's point of view.
        var row = await harness.Db.DungeonRuns.SingleAsync(
            r => r.Id == run.Run.Id, TestContext.Current.CancellationToken);

        row.DungeonKey = "flooded-annexe";
        await harness.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await WoundAsync(harness.Db, entered.Value!.Encounter!, 1);

        var goldBefore = (await CharacterAsync(harness)).Gold;

        var round = await harness.Combat.AttackAsync(
            harness.UserId, entered.Value.Encounter!.Id, TestContext.Current.CancellationToken);

        Assert.Equal(EncounterStatus.Won, round.Value!.Encounter.Status);

        var closed = await harness.Db.DungeonRuns.AsNoTracking()
            .SingleAsync(r => r.Id == run.Run.Id, TestContext.Current.CancellationToken);

        Assert.Equal(DungeonRunStatus.Cleared, closed.Status);
        Assert.NotNull(closed.EndedAt);

        // Nothing was paid for a dungeon nothing knows about, and no die was spent trying: the
        // twenty gold is the Hedge Troll's own purse, eighteen plus the three on its d28.
        Assert.Equal(0, closed.GoldAwarded);
        Assert.Equal(goldBefore + 20, (await CharacterAsync(harness)).Gold);
        Assert.Equal(6, script.RollCount);

        // And the slot is free, which was the whole point.
        Assert.Null(await harness.Dungeons.ActiveAsync(
            harness.UserId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_dungeon_below_the_characters_level_is_refused_before_anything_is_rolled()
    {
        var script = new SequenceDiceRoller(1, 1);
        var harness = await ArrangeAsync(script, level: 2);

        var refused = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.DragonsReach, TestContext.Current.CancellationToken);

        Assert.False(refused.Ok);
        Assert.Equal(RpgFailure.MonsterOutOfRange, refused.Failure);
        Assert.Equal(0, script.RollCount);

        var missing = await harness.Dungeons.StartAsync(
            harness.UserId, "the-back-of-the-wardrobe", TestContext.Current.CancellationToken);

        Assert.False(missing.Ok);
        Assert.Equal(RpgFailure.NotFound, missing.Failure);
        Assert.Equal(0, script.RollCount);
    }

    /// <summary>Nothing about a dungeon may pay experience (DEC-012).</summary>
    [Fact]
    public async Task Clearing_a_dungeon_never_moves_experience_or_level()
    {
        var script = new SequenceDiceRoller(
            1, 1,
            18, 4, 3, 100,
            18, 4, 3, 100,
            18, 4, 20, 100, 1, 1, 1);

        var harness = await ArrangeAsync(script);

        var before = await CharacterAsync(harness);

        var started = await harness.Dungeons.StartAsync(
            harness.UserId, DungeonCatalog.SunkenWarren, TestContext.Current.CancellationToken);

        for (var room = 0; room < 3; room++)
        {
            var entered = await harness.Dungeons.EnterAsync(
                harness.UserId, started.Value!.Run.Id, TestContext.Current.CancellationToken);

            await WoundAsync(harness.Db, entered.Value!.Encounter!, 1);

            await harness.Combat.AttackAsync(
                harness.UserId, entered.Value.Encounter!.Id, TestContext.Current.CancellationToken);
        }

        var after = await CharacterAsync(harness);

        Assert.Equal(before.TotalXp, after.TotalXp);
        Assert.Equal(LevelCurve.LevelForXp(before.TotalXp), LevelCurve.LevelForXp(after.TotalXp));

        // And it did pay, so the assertion above is about a run that actually finished.
        Assert.True(after.Gold > before.Gold);
    }
}

/// <summary>
/// The five routes, over HTTP, with the real wiring behind them.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class DungeonEndpointTests(PostgresFixture postgres) : IAsyncLifetime
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

    private static async Task ChooseClassAsync(HttpClient client)
    {
        var response = await client.PutAsJsonAsync(
            "/api/rpg/class", new { classKey = ClassCatalog.Fighter });

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Buys rooms the only way anything can: by finishing real work. Each Epic task is five
    /// stamina and enough experience to open the Sunken Warren.
    /// </summary>
    private static async Task<int> WorkToLevelTwoAsync(HttpClient client)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var created = await client.PostAsJsonAsync(
                "/api/tasks", new { title = "Real work", difficulty = "epic" });

            var task = await created.Content.ReadFromJsonAsync<IdDto>();

            var completed = await client.PostAsJsonAsync(
                $"/api/tasks/{task!.Id}/complete", new { utcOffsetMinutes = 0 });

            completed.EnsureSuccessStatusCode();

            var character = await client.GetFromJsonAsync<CharacterDto>("/api/character");

            if (character!.Level >= 2)
            {
                return character.Level;
            }
        }

        throw new InvalidOperationException("Ten Epic tasks did not reach level two.");
    }

    private static async Task<DungeonRunDto> StartRunAsync(HttpClient client)
    {
        var started = await client.PostAsJsonAsync(
            "/api/rpg/dungeons", new { dungeonKey = DungeonCatalog.SunkenWarren });

        Assert.Equal(HttpStatusCode.Created, started.StatusCode);

        return (await started.Content.ReadFromJsonAsync<DungeonRunDto>())!;
    }

    [Fact]
    public async Task Every_dungeon_route_requires_authentication()
    {
        using var anonymous = _factory.CreateAnonymousClient();
        var id = Guid.NewGuid();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/rpg/dungeons")).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/rpg/dungeons/active")).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync(
                "/api/rpg/dungeons", new { dungeonKey = DungeonCatalog.SunkenWarren })).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync($"/api/rpg/dungeons/{id}/enter", null)).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync($"/api/rpg/dungeons/{id}/abandon", null)).StatusCode);
    }

    [Fact]
    public async Task The_board_shows_nothing_until_the_work_has_been_done()
    {
        await ChooseClassAsync(_alice);

        // Level one, so nothing is open yet.
        Assert.Empty((await _alice.GetFromJsonAsync<List<DungeonDto>>("/api/rpg/dungeons"))!);

        await WorkToLevelTwoAsync(_alice);

        var board = (await _alice.GetFromJsonAsync<List<DungeonDto>>("/api/rpg/dungeons"))!;
        var warren = Assert.Single(board, d => d.Key == DungeonCatalog.SunkenWarren);

        Assert.Equal("The Sunken Warren", warren.Name);
        Assert.Equal(3, warren.Rooms);
        Assert.Equal(3, warren.TotalStaminaCost);
        Assert.Equal("Hedge Troll", warren.BossName);

        // The deeper two are still shut, and shut is a level gate rather than a hidden feature.
        Assert.DoesNotContain(board, d => d.Key == DungeonCatalog.DragonsReach);
    }

    /// <summary>
    /// The reload story, end to end: the client holds nothing between requests.
    /// </summary>
    /// <remarks>
    /// A second client with the same credentials stands in for a page reload, which is the case
    /// that matters: everything needed to pick the run back up comes off the row and the count,
    /// not out of anything the first client was holding.
    /// </remarks>
    [Fact]
    public async Task A_run_survives_a_reload_with_its_rooms_and_its_open_fight()
    {
        await ChooseClassAsync(_alice);
        await WorkToLevelTwoAsync(_alice);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await _alice.GetAsync("/api/rpg/dungeons/active")).StatusCode);

        var run = await StartRunAsync(_alice);

        Assert.Equal(3, run.Rooms.Count);
        Assert.Equal(MonsterCatalog.HedgeTroll, run.Rooms[2].MonsterKey);
        Assert.Equal(["current", "ahead", "ahead"], run.Rooms.Select(r => r.State));
        Assert.Equal(0, run.Depth);
        Assert.Null(run.Encounter);

        var entered = await _alice.PostAsync($"/api/rpg/dungeons/{run.Id}/enter", null);

        entered.EnsureSuccessStatusCode();

        var opened = (await entered.Content.ReadFromJsonAsync<DungeonRunDto>())!;

        Assert.NotNull(opened.Encounter);
        Assert.Equal(run.Rooms[0].MonsterKey, opened.Encounter.MonsterKey);
        Assert.Equal("active", opened.Encounter.Status);

        using var reloaded = _factory.CreateClientAs("auth0|alice");

        var resumed = await reloaded.GetFromJsonAsync<DungeonRunDto>("/api/rpg/dungeons/active");

        Assert.Equal(run.Id, resumed!.Id);
        Assert.Equal(run.Rooms.Select(r => r.MonsterKey), resumed.Rooms.Select(r => r.MonsterKey));
        Assert.Equal(opened.Encounter.Id, resumed.Encounter!.Id);

        // And the fight it carries is resumable through the ordinary attack route, which is the
        // whole point of a room being an ordinary encounter.
        var attacked = await reloaded.PostAsync(
            $"/api/rpg/encounters/{resumed.Encounter.Id}/attack", null);

        attacked.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task The_tavern_is_shut_while_a_run_is_open_and_opens_again_when_it_is_not()
    {
        await ChooseClassAsync(_alice);
        await WorkToLevelTwoAsync(_alice);

        var run = await StartRunAsync(_alice);

        var tavern = await _alice.PostAsJsonAsync(
            "/api/rpg/encounters", new { monsterKey = MonsterCatalog.GiantRat });

        Assert.Equal(HttpStatusCode.Conflict, tavern.StatusCode);

        var second = await _alice.PostAsJsonAsync(
            "/api/rpg/dungeons", new { dungeonKey = DungeonCatalog.SunkenWarren });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var abandoned = await _alice.PostAsync($"/api/rpg/dungeons/{run.Id}/abandon", null);

        abandoned.EnsureSuccessStatusCode();

        Assert.Equal(
            "abandoned",
            (await abandoned.Content.ReadFromJsonAsync<DungeonRunDto>())!.Status);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await _alice.GetAsync("/api/rpg/dungeons/active")).StatusCode);

        var reopened = await _alice.PostAsJsonAsync(
            "/api/rpg/encounters", new { monsterKey = MonsterCatalog.GiantRat });

        Assert.Equal(HttpStatusCode.Created, reopened.StatusCode);
    }

    [Fact]
    public async Task Entering_a_finished_run_is_a_conflict_rather_than_a_second_chance()
    {
        await ChooseClassAsync(_alice);
        await WorkToLevelTwoAsync(_alice);

        var run = await StartRunAsync(_alice);

        (await _alice.PostAsync($"/api/rpg/dungeons/{run.Id}/abandon", null)).EnsureSuccessStatusCode();

        Assert.Equal(
            HttpStatusCode.Conflict,
            (await _alice.PostAsync($"/api/rpg/dungeons/{run.Id}/enter", null)).StatusCode);

        Assert.Equal(
            HttpStatusCode.Conflict,
            (await _alice.PostAsync($"/api/rpg/dungeons/{run.Id}/abandon", null)).StatusCode);
    }

    [Fact]
    public async Task Another_persons_run_is_indistinguishable_from_one_that_never_existed()
    {
        await ChooseClassAsync(_alice);
        await WorkToLevelTwoAsync(_alice);
        await ChooseClassAsync(_bob);
        await WorkToLevelTwoAsync(_bob);

        var alices = await StartRunAsync(_alice);

        // 404 rather than 403, so run ids cannot be probed for existence.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _bob.PostAsync($"/api/rpg/dungeons/{alices.Id}/enter", null)).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _bob.PostAsync($"/api/rpg/dungeons/{alices.Id}/abandon", null)).StatusCode);

        // Bob has no run of his own, and Alice's is untouched.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await _bob.GetAsync("/api/rpg/dungeons/active")).StatusCode);

        var stillThere = await _alice.GetFromJsonAsync<DungeonRunDto>("/api/rpg/dungeons/active");

        Assert.Equal(alices.Id, stillThere!.Id);
        Assert.Equal("active", stillThere.Status);
    }

    [Fact]
    public async Task A_dungeon_that_is_not_open_yet_is_a_bad_request()
    {
        await ChooseClassAsync(_alice);
        await WorkToLevelTwoAsync(_alice);

        var refused = await _alice.PostAsJsonAsync(
            "/api/rpg/dungeons", new { dungeonKey = DungeonCatalog.DragonsReach });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var missing = await _alice.PostAsJsonAsync(
            "/api/rpg/dungeons", new { dungeonKey = "the-back-of-the-wardrobe" });

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _alice.PostAsync($"/api/rpg/dungeons/{Guid.NewGuid()}/enter", null)).StatusCode);
    }

    /// <summary>
    /// A missing run says so. The 404 carries the service's sentence rather than a status phrase.
    /// </summary>
    /// <remarks>
    /// The arm sat under a comment promising the two 404s would "stay one answer while keeping
    /// their own message" and then threw the message away, so the screen printed the generic
    /// ProblemDetails title UseStatusCodePages backfills. Carrying it gives nothing away: a run
    /// that never existed and one belonging to somebody else produce this same failure with this
    /// same sentence, which is what keeps run ids unprobeable, and the pair is asserted here
    /// rather than assumed.
    /// </remarks>
    [Fact]
    public async Task A_missing_run_answers_with_its_own_sentence()
    {
        await ChooseClassAsync(_alice);
        await WorkToLevelTwoAsync(_alice);
        await ChooseClassAsync(_bob);
        await WorkToLevelTwoAsync(_bob);

        var alices = await StartRunAsync(_alice);

        var invented = await _bob.PostAsync($"/api/rpg/dungeons/{Guid.NewGuid()}/enter", null);
        var somebody = await _bob.PostAsync($"/api/rpg/dungeons/{alices.Id}/enter", null);

        Assert.Equal(HttpStatusCode.NotFound, invented.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, somebody.StatusCode);

        var inventedBody = (await invented.Content.ReadFromJsonAsync<ProblemDto>())!;
        var somebodyBody = (await somebody.Content.ReadFromJsonAsync<ProblemDto>())!;

        Assert.Equal("No such dungeon run.", inventedBody.Detail);

        // Byte for byte the same answer, which is the property that matters more than the words.
        Assert.Equal(inventedBody.Detail, somebodyBody.Detail);
    }

    private sealed record IdDto(Guid Id);

    private sealed record ProblemDto(string? Detail);

    private sealed record CharacterDto(int Level, int TotalXp);

    private sealed record DungeonDto(
        string Key,
        string Name,
        int Level,
        int Rooms,
        string BossName,
        int ClearGold,
        string RewardFloor,
        int StaminaPerRoom,
        int TotalStaminaCost);

    private sealed record DungeonRoomDto(int Index, string MonsterKey, string MonsterName, string State);

    private sealed record EncounterDto(Guid Id, string MonsterKey, string Status, int Round, List<StatusEffectDto> Effects);

    private sealed record StatusEffectDto(string Kind, string Target, int Rounds, int Magnitude, string Source);

    private sealed record DungeonRunDto(
        Guid Id,
        string DungeonKey,
        string Name,
        string Status,
        List<DungeonRoomDto> Rooms,
        int Depth,
        int GoldAwarded,
        EncounterDto? Encounter);
}
