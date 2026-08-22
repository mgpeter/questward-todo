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
/// Beginning again: what it costs, what it pays, and what it is not allowed to take.
/// </summary>
/// <remarks>
/// Ascending is the only destructive act in the game layer, so most of what is asserted here is
/// what survives it. The two halves matter equally: a wipe that missed a table would leave a new
/// era carrying the old one's gear, and a wipe that took one row too many would delete the record
/// of real work that DEC-012 exists to protect.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class AscendTests(PostgresFixture postgres)
{
    private sealed record Harness(
        TodoDbContext Db,
        AscendService Ascend,
        CombatService Combat,
        GamificationService Gamification,
        Guid UserId);

    private async Task<Harness> ArrangeAsync(int level = 10)
    {
        await postgres.ResetAsync();
        var user = await postgres.CreateUserAsync("test|ascendant");

        var db = postgres.CreateContext();
        var roller = new SequenceDiceRoller(Enumerable.Repeat(20, 4000).ToArray());
        var sheets = new CharacterSheetService(db);
        var loot = new LootService(db, roller);
        var chronicle = new ChronicleService(db);
        var quests = new QuestService(db, loot, chronicle);
        var adventurer = new AdventurerService(db, sheets, loot);
        var combat = new CombatService(db, roller, sheets, loot, quests, chronicle);
        var gamification = new GamificationService(db, new AchievementEvaluator(), quests, chronicle);
        var ascend = new AscendService(db, sheets, adventurer, chronicle);

        await adventurer.ChooseClassAsync(user.Id, ClassCatalog.Fighter, default);

        var harness = new Harness(db, ascend, combat, gamification, user.Id);

        await ReachLevelAsync(harness, level);

        return harness;
    }

    /// <summary>
    /// Raises the character the only way anything may, by finishing real work (DEC-012).
    /// </summary>
    private static async Task ReachLevelAsync(Harness harness, int level)
    {
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

            await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);
        }
    }

    private static Task<Character> CharacterAsync(Harness harness) =>
        harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId, TestContext.Current.CancellationToken);

    /// <summary>
    /// Something this character is actually allowed to fight.
    /// </summary>
    /// <remarks>
    /// The bestiary's first entry is a Giant Rat and these tests run at level ten, where the
    /// availability band has moved past it. Asking the catalog what is in band keeps the test
    /// about ascending rather than about which monster happens to be first in the list.
    /// </remarks>
    private static async Task<string> InBandMonsterAsync(Harness harness)
    {
        var character = await CharacterAsync(harness);

        return MonsterCatalog.AvailableAt(LevelCurve.LevelForXp(character.TotalXp))[0].Key;
    }

    private static async Task GiveAsync(Harness harness, int gold, int stamina)
    {
        var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);

        character.Gold = gold;
        character.Stamina = stamina;

        await harness.Db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // The gate
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_character_below_the_gate_cannot_ascend_and_loses_nothing()
    {
        var harness = await ArrangeAsync(level: 2);
        await GiveAsync(harness, gold: 900, stamina: 40);

        var result = await harness.Ascend.AscendAsync(harness.UserId, default);

        Assert.False(result.Ok);
        Assert.Equal(RpgFailure.NotReadyToAscend, result.Failure);
        Assert.Contains(AscendRules.MinimumLevel.ToString(), result.Message);

        var character = await CharacterAsync(harness);

        Assert.Equal(900, character.Gold);
        Assert.Equal(40, character.Stamina);
        Assert.Equal(0, character.Ascensions);
        Assert.Empty(await harness.Db.ChronicleEntries
            .Where(e => e.Kind == ChronicleKind.Ascended)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A fight in progress is refused rather than deleted underneath itself.
    /// </summary>
    /// <remarks>
    /// The wipe would take the encounter with everything else, and the tab it was open in would
    /// answer 404 mid-round with nothing to explain it. One click to finish or withdraw is
    /// legible; a fight that vanishes is not.
    /// </remarks>
    [Fact]
    public async Task Ascending_is_refused_while_a_fight_is_open()
    {
        var harness = await ArrangeAsync();
        await GiveAsync(harness, gold: 100, stamina: 5);

        var started = await harness.Combat.StartAsync(
            harness.UserId, await InBandMonsterAsync(harness), default);
        Assert.True(started.Ok, started.Message);

        var result = await harness.Ascend.AscendAsync(harness.UserId, default);

        Assert.False(result.Ok);
        Assert.Equal(RpgFailure.EncounterAlreadyActive, result.Failure);

        // The fight is still there and still winnable.
        Assert.NotNull(await harness.Combat.ActiveAsync(harness.UserId, default));
    }

    // -------------------------------------------------------------------------
    // The exchange
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Gold_and_stamina_and_the_level_render_down_to_essence()
    {
        var harness = await ArrangeAsync();
        await GiveAsync(harness, gold: 1_234, stamina: 47);

        var before = await CharacterAsync(harness);
        var level = LevelCurve.LevelForXp(before.TotalXp);
        var expected = AscendRules.EssenceFor(1_234, 47, level);

        var result = await harness.Ascend.AscendAsync(harness.UserId, default);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(expected, result.Value!.EssenceGained);

        // 123 from the gold, 9 from the stamina, and five a level. Written out so a change to
        // the rate has to be made deliberately in two places rather than absorbed silently here.
        Assert.Equal(123 + 9 + (level * AscendRules.EssencePerLevel), result.Value.EssenceGained);

        var after = await CharacterAsync(harness);

        // Added to the balance, never assigned over it: essence already earned at the forge
        // belongs to the player as much as this does.
        Assert.Equal(before.Essence + expected, after.Essence);
        Assert.Equal(0, after.Gold);
        Assert.Equal(0, after.Stamina);
        Assert.Equal(0, after.TotalXp);
        Assert.Equal(1, LevelCurve.LevelForXp(after.TotalXp));
        Assert.Equal(1, after.Ascensions);
        Assert.NotNull(after.AscendedAt);
    }

    [Fact]
    public async Task Essence_already_held_is_added_to_rather_than_replaced()
    {
        var harness = await ArrangeAsync();
        await GiveAsync(harness, gold: 0, stamina: 0);

        var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);
        character.Essence = 500;
        await harness.Db.SaveChangesAsync();

        var result = await harness.Ascend.AscendAsync(harness.UserId, default);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(500 + result.Value!.EssenceGained, (await CharacterAsync(harness)).Essence);
    }

    // -------------------------------------------------------------------------
    // What goes, and what stays
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_era_is_deleted_and_the_record_of_real_work_is_not()
    {
        var harness = await ArrangeAsync();
        await GiveAsync(harness, gold: 300, stamina: 10);

        // Something in every table the wipe reaches, produced the way the game produces it.
        var started = await harness.Combat.StartAsync(
            harness.UserId, await InBandMonsterAsync(harness), default);

        Assert.True(started.Ok, started.Message);

        await harness.Combat.FleeAsync(harness.UserId, started.Value!.Id, default);

        var before = await CharacterAsync(harness);
        var badgesBefore = await harness.Db.AchievementUnlocks
            .CountAsync(a => a.UserId == harness.UserId, TestContext.Current.CancellationToken);

        Assert.NotEmpty(await harness.Db.InventoryItems
            .Where(i => i.UserId == harness.UserId)
            .ToListAsync(TestContext.Current.CancellationToken));

        var result = await harness.Ascend.AscendAsync(harness.UserId, default);
        Assert.True(result.Ok, result.Message);

        var token = TestContext.Current.CancellationToken;

        Assert.Empty(await harness.Db.Encounters.Where(e => e.UserId == harness.UserId).ToListAsync(token));
        Assert.Empty(await harness.Db.BestiaryEntries.Where(b => b.UserId == harness.UserId).ToListAsync(token));
        Assert.Empty(await harness.Db.QuestProgress.Where(q => q.UserId == harness.UserId).ToListAsync(token));
        Assert.Empty(await harness.Db.HuntContracts.Where(c => c.UserId == harness.UserId).ToListAsync(token));
        Assert.Empty(await harness.Db.DungeonRuns.Where(r => r.UserId == harness.UserId).ToListAsync(token));

        // The work survives: the tasks that were finished, the count behind the badges, and the
        // badges themselves.
        Assert.NotEmpty(await harness.Db.Tasks.Where(t => t.UserId == harness.UserId).ToListAsync(token));

        var after = await CharacterAsync(harness);

        Assert.Equal(before.TasksCompleted, after.TasksCompleted);
        Assert.Equal(
            badgesBefore,
            await harness.Db.AchievementUnlocks.CountAsync(a => a.UserId == harness.UserId, token));
        Assert.True(badgesBefore > 0, "The arrangement should have unlocked at least one badge.");
    }

    [Fact]
    public async Task The_class_stays_and_its_starting_gear_is_handed_back()
    {
        var harness = await ArrangeAsync();
        await GiveAsync(harness, gold: 0, stamina: 0);

        var fighter = ClassCatalog.Find(ClassCatalog.Fighter)!;

        await harness.Ascend.AscendAsync(harness.UserId, default);

        var character = await CharacterAsync(harness);

        Assert.Equal(ClassCatalog.Fighter, character.ClassKey);
        Assert.Equal(fighter.StartingScores, character.AbilityScores);

        var items = await harness.Db.InventoryItems
            .AsNoTracking()
            .Where(i => i.UserId == harness.UserId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.True(item.IsEquipped));
        Assert.Contains(items, item => item.ItemKey == fighter.StartingWeaponKey);
        Assert.Contains(items, item => item.ItemKey == fighter.StartingArmourKey);

        // Not left on one hit point: the first thing a new era asks should not be a wait.
        Assert.True(character.CurrentHitPoints > 1);
    }

    /// <summary>
    /// The chronicle is the one thing that crosses the line, which is the point of it.
    /// </summary>
    [Fact]
    public async Task The_chronicle_survives_and_the_ascension_closes_the_era_it_ended()
    {
        var harness = await ArrangeAsync();

        var started = await harness.Combat.StartAsync(
            harness.UserId, await InBandMonsterAsync(harness), default);

        Assert.True(started.Ok, started.Message);

        await harness.Combat.FleeAsync(harness.UserId, started.Value!.Id, default);

        // Set after the fight, which has already spent a stamina of its own, so the numbers the
        // entry records are exactly the ones ascending converted.
        await GiveAsync(harness, gold: 250, stamina: 20);

        var result = await harness.Ascend.AscendAsync(harness.UserId, default);
        Assert.True(result.Ok, result.Message);

        var entries = await harness.Db.ChronicleEntries
            .AsNoTracking()
            .Where(e => e.UserId == harness.UserId)
            .ToListAsync(TestContext.Current.CancellationToken);

        // The fight from before the ascension is still readable, minus the log it pointed at.
        var fight = Assert.Single(entries, e => e.Kind == ChronicleKind.FightFled);

        Assert.Null(fight.EncounterId);
        Assert.Equal(0, fight.Era);

        var ascended = Assert.Single(entries, e => e.Kind == ChronicleKind.Ascended);
        var facts = ChronicleService.ReadFacts(ascended);

        // Era 0, not 1: the entry belongs to the age it ended, which is what puts the feed's
        // divider above it rather than below.
        Assert.Equal(0, ascended.Era);
        Assert.Equal("250", facts[ChronicleNarrator.GoldKey]);
        Assert.Equal("20", facts[ChronicleNarrator.StaminaKey]);
        Assert.Equal(result.Value!.EssenceGained.ToString(), facts[ChronicleNarrator.EssenceKey]);
        Assert.Equal("1", facts[ChronicleNarrator.OrdinalKey]);
    }

    [Fact]
    public async Task Entries_written_after_an_ascension_carry_the_new_era()
    {
        var harness = await ArrangeAsync();
        await GiveAsync(harness, gold: 0, stamina: 5);

        await harness.Ascend.AscendAsync(harness.UserId, default);

        // Stamina went with the era, so the new one needs some before it can fight at all.
        await GiveAsync(harness, gold: 0, stamina: 5);

        var started = await harness.Combat.StartAsync(
            harness.UserId, await InBandMonsterAsync(harness), default);

        Assert.True(started.Ok, started.Message);

        await harness.Combat.FleeAsync(harness.UserId, started.Value!.Id, default);

        var fled = await harness.Db.ChronicleEntries
            .AsNoTracking()
            .Where(e => e.UserId == harness.UserId && e.Kind == ChronicleKind.FightFled)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, Assert.Single(fled).Era);
    }

    [Fact]
    public async Task Ascending_touches_nobody_else()
    {
        var harness = await ArrangeAsync();
        var stranger = await postgres.CreateUserAsync("test|bystander");

        harness.Db.Tasks.Add(new TodoTask { UserId = stranger.Id, Title = "Their own business" });
        await harness.Db.SaveChangesAsync();

        await GiveAsync(harness, gold: 100, stamina: 10);
        await harness.Ascend.AscendAsync(harness.UserId, default);

        var token = TestContext.Current.CancellationToken;

        Assert.NotEmpty(await harness.Db.Tasks.Where(t => t.UserId == stranger.Id).ToListAsync(token));
        Assert.Equal(0, (await harness.Db.Characters.SingleAsync(c => c.UserId == stranger.Id, token)).Ascensions);
    }
}
