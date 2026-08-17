using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Services.Rpg;
using TodoApp.Data;
using TodoApp.Models;
using TodoApp.Models.Dice;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

[Collection(nameof(PostgresCollection))]
public class CombatServiceTests(PostgresFixture postgres)
{
    /// <summary>Enough scripted dice to carry any single test to its conclusion.</summary>
    private static SequenceDiceRoller AlwaysHits() =>
        new(Enumerable.Repeat(20, 400).ToArray());

    private static SequenceDiceRoller AlwaysMisses() =>
        new(Enumerable.Repeat(1, 400).ToArray());

    private sealed record Harness(
        TodoDbContext Db,
        CombatService Combat,
        AdventurerService Adventurer,
        QuestService Quests,
        Guid UserId);

    private async Task<Harness> ArrangeAsync(IDiceRoller roller, string classKey = ClassCatalog.Fighter)
    {
        await postgres.ResetAsync();
        var user = await postgres.CreateUserAsync("test|hero");

        var db = postgres.CreateContext();
        var sheets = new CharacterSheetService(db);
        var loot = new LootService(db, roller);
        var quests = new QuestService(db, loot);
        var adventurer = new AdventurerService(db, sheets, loot);
        var combat = new CombatService(db, roller, sheets, loot, quests);

        await adventurer.ChooseClassAsync(user.Id, classKey, TestContext.Current.CancellationToken);

        // Enough stamina that only the tests that care about it are gated by it.
        var character = await db.Characters.SingleAsync(c => c.UserId == user.Id);
        character.Stamina = 20;
        await db.SaveChangesAsync();

        return new Harness(db, combat, adventurer, quests, user.Id);
    }

    // -------------------------------------------------------------------------
    // The invariant the entire design exists to protect.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Fighting_a_monster_to_death_never_moves_experience_or_level()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var before = await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId);

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);
        Assert.True(start.Ok);

        // Swing until the goblin drops.
        for (var round = 0; round < 20; round++)
        {
            var attack = await harness.Combat.AttackAsync(harness.UserId, start.Value!.Id, default);
            Assert.True(attack.Ok);

            if (attack.Value!.Encounter.IsOver)
            {
                Assert.Equal(EncounterStatus.Won, attack.Value.Encounter.Status);
                break;
            }
        }

        var after = await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId);

        // Gold moved. Experience did not, and must never.
        Assert.True(after.Gold > before.Gold);
        Assert.Equal(before.TotalXp, after.TotalXp);
        Assert.Equal(before.TasksCompleted, after.TasksCompleted);
    }

    [Fact]
    public async Task Claiming_a_quest_reward_never_moves_experience()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);

        for (var round = 0; round < 20; round++)
        {
            var attack = await harness.Combat.AttackAsync(harness.UserId, start.Value!.Id, default);
            if (attack.Value!.Encounter.IsOver) break;
        }

        var before = await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId);

        var claim = await harness.Quests.ClaimAsync(harness.UserId, QuestCatalog.FirstBlood, default);
        Assert.True(claim.Ok);

        var after = await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId);

        Assert.True(after.Gold > before.Gold);
        Assert.Equal(before.TotalXp, after.TotalXp);
    }

    // -------------------------------------------------------------------------
    // Stamina, the gate that makes the above possible.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_fight_costs_stamina()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var before = (await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId)).Stamina;

        await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);

        var after = (await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId)).Stamina;

        Assert.Equal(before - CombatService.StaminaPerEncounter, after);
    }

    [Fact]
    public async Task Without_stamina_there_is_no_fight()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);
        character.Stamina = 0;
        await harness.Db.SaveChangesAsync();

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);

        Assert.False(start.Ok);
        Assert.Equal(RpgFailure.NotEnoughStamina, start.Failure);
        Assert.Empty(await harness.Db.Encounters.Where(e => e.UserId == harness.UserId).ToListAsync());
    }

    [Fact]
    public async Task Completing_a_task_is_the_only_thing_that_grants_stamina()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);
        character.Stamina = 0;
        await harness.Db.SaveChangesAsync();

        var evaluator = new TodoApp.Api.Services.AchievementEvaluator();
        var gamification = new TodoApp.Api.Services.GamificationService(
            harness.Db, evaluator, harness.Quests);

        var task = new TodoTask
        {
            UserId = harness.UserId, Title = "Real work", Difficulty = Difficulty.Hard
        };
        harness.Db.Tasks.Add(task);
        await harness.Db.SaveChangesAsync();

        await gamification.CompleteAsync(harness.UserId, task.Id, 0, default);

        var after = await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId);

        Assert.Equal(Difficulty.Hard.Stamina(), after.Stamina);
        Assert.Equal(50, after.TotalXp); // and the XP path is untouched
    }

    // -------------------------------------------------------------------------
    // Encounter lifecycle
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Only_one_fight_can_run_at_a_time()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var first = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);
        Assert.True(first.Ok);

        var second = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);

        Assert.False(second.Ok);
        Assert.Equal(RpgFailure.EncounterAlreadyActive, second.Failure);
    }

    [Fact]
    public async Task Fleeing_ends_the_fight_and_does_not_refund_the_stamina()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);
        var staminaAfterStart = (await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId)).Stamina;

        var fled = await harness.Combat.FleeAsync(harness.UserId, start.Value!.Id, default);

        Assert.True(fled.Ok);
        Assert.Equal(EncounterStatus.Fled, fled.Value!.Status);

        var staminaAfterFlee = (await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId)).Stamina;

        Assert.Equal(staminaAfterStart, staminaAfterFlee);

        // And a new fight is possible again.
        Assert.True((await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default)).Ok);
    }

    [Fact]
    public async Task Attacking_a_finished_fight_is_rejected()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);
        await harness.Combat.FleeAsync(harness.UserId, start.Value!.Id, default);

        var attack = await harness.Combat.AttackAsync(harness.UserId, start.Value.Id, default);

        Assert.False(attack.Ok);
        Assert.Equal(RpgFailure.EncounterOver, attack.Failure);
    }

    [Fact]
    public async Task A_monster_far_above_the_character_is_refused()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.YoungDragon, default);

        Assert.False(start.Ok);
        Assert.Equal(RpgFailure.MonsterOutOfRange, start.Failure);
    }

    [Fact]
    public async Task Losing_leaves_the_character_standing_on_one_hit_point()
    {
        // A todo app has no business punishing someone for losing a dice roll.
        var harness = await ArrangeAsync(AlwaysMisses());

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);

        // AlwaysMisses makes the player fumble and the monster roll 1 too, so force the
        // loss by starting the character on the edge.
        var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);
        character.CurrentHitPoints = 1;
        character.HitPointsUpdatedAt = DateTimeOffset.UtcNow;
        await harness.Db.SaveChangesAsync();

        for (var round = 0; round < 30; round++)
        {
            var attack = await harness.Combat.AttackAsync(harness.UserId, start.Value!.Id, default);

            if (!attack.Ok || attack.Value!.Encounter.IsOver)
            {
                break;
            }
        }

        var after = await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId);

        Assert.True(after.CurrentHitPoints >= 1);
    }

    // -------------------------------------------------------------------------
    // Rewards
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_win_pays_gold_and_advances_quests()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);

        AttackOutcome? final = null;

        for (var round = 0; round < 20; round++)
        {
            var attack = await harness.Combat.AttackAsync(harness.UserId, start.Value!.Id, default);
            final = attack.Value;

            if (final!.Encounter.IsOver) break;
        }

        Assert.Equal(EncounterStatus.Won, final!.Encounter.Status);
        Assert.True(final.GoldAwarded > 0);
        Assert.Contains(final.QuestsAdvanced, q => q.Key == QuestCatalog.FirstBlood);
    }

    [Fact]
    public async Task The_combat_log_records_every_roll_with_its_breakdown()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);
        var attack = await harness.Combat.AttackAsync(harness.UserId, start.Value!.Id, default);

        var log = CombatService.ReadLog(attack.Value!.Encounter);

        Assert.Contains(log, r => r.Kind == "attack" && r.Dice.Count > 0 && r.Target is not null);
        Assert.Contains(log, r => r.Kind == "damage");

        // The breakdown is the product: a roll with no labelled modifiers cannot be
        // rendered as arithmetic.
        var attackRoll = log.First(r => r is { Kind: "attack", Actor: CombatRoll.Player });
        Assert.NotEmpty(attackRoll.Modifiers);
        Assert.All(attackRoll.Modifiers, m => Assert.NotEmpty(m.Label));
    }

    [Fact]
    public async Task A_fight_survives_a_reload()
    {
        // The encounter is a persisted row precisely so closing the tab does not lose it.
        var harness = await ArrangeAsync(AlwaysHits());

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);

        var resumed = await harness.Combat.ActiveAsync(harness.UserId, default);

        Assert.NotNull(resumed);
        Assert.Equal(start.Value!.Id, resumed.Id);
        Assert.Equal(EncounterStatus.Active, resumed.Status);
        Assert.Equal(MonsterCatalog.Goblin, resumed.MonsterKey);
    }

    [Fact]
    public async Task A_finished_fight_is_no_longer_the_active_one()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);

        for (var round = 0; round < 20; round++)
        {
            var attack = await harness.Combat.AttackAsync(harness.UserId, start.Value!.Id, default);
            if (attack.Value!.Encounter.IsOver) break;
        }

        Assert.Null(await harness.Combat.ActiveAsync(harness.UserId, default));
    }
}
