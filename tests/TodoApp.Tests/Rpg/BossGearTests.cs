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
/// Which gear a boss is in, where that number comes from, and the one direction it is allowed
/// to move.
/// </summary>
/// <remarks>
/// Two facts have to hold together and they are in tension. The phase a boss belongs in is
/// derived from its hit points, which is the DEC-002 shape; the phase a fight has entered is
/// stored on the encounter, which is not. They are reconciled by the stored value being a
/// high-water mark of the derived one, and the whole reason it exists is the case where the two
/// deliberately disagree.
/// <para>
/// <see cref="MonsterPhaseRuleTests"/> covers the arithmetic at both ends of the bestiary and
/// <see cref="BossPhaseTests"/> covers entry, so what is left is the boundary of every gear the
/// catalog actually declares, and the loop the high-water mark exists to prevent, driven by the
/// mechanic that causes it rather than by a test reaching in and setting hit points.
/// </para>
/// </remarks>
public class BossGearRuleTests
{
    /// <summary>
    /// Every declared gear turns on at the exact hit point the catalog names, and not one
    /// sooner.
    /// </summary>
    /// <remarks>
    /// A sweep rather than a worked example, so a boss added later inherits the guard instead of
    /// needing somebody to remember to write one. The pair of assertions is what makes it a
    /// boundary rather than a smoke test: a rounding change that moved any threshold by a single
    /// hit point would still satisfy either one on its own.
    /// </remarks>
    [Fact]
    public void Every_gear_in_the_bestiary_turns_on_the_hit_point_the_catalog_names()
    {
        var withGears = MonsterCatalog.All.Where(m => m.Phases is not null).ToList();

        // A sweep over an empty list asserts nothing at all, and the four bosses are the only
        // reason any of this code exists.
        Assert.NotEmpty(withGears);

        Assert.All(withGears, monster =>
        {
            // Full health is no gear, and a corpse has been through all of them.
            Assert.Equal(0, monster.PhaseAt(monster.MaxHitPoints));
            Assert.Equal(monster.Phases!.Count, monster.PhaseAt(0));

            for (var index = 0; index < monster.Phases.Count; index++)
            {
                var phase = monster.Phases[index];

                // The highest hit point total that is still at or under the threshold. Integer
                // division floors, which is the same direction the cross-multiplication takes.
                var onIt = phase.AtPercent * monster.MaxHitPoints / 100;

                Assert.Equal(index + 1, monster.PhaseAt(onIt));
                Assert.Equal(index, monster.PhaseAt(onIt + 1));

                // And the number a fight stores reads back as the definition it names, which is
                // what the wire and the entry loop both depend on.
                Assert.Same(phase, monster.PhaseDefinition(index + 1));
            }

            // One past the last gear is nothing, so a stored number that outran the catalog
            // reads as no gear rather than throwing at whoever renders it.
            Assert.Null(monster.PhaseDefinition(monster.Phases.Count + 1));
        });
    }
}

/// <summary>The high-water mark, driven by the boss that heals itself.</summary>
[Collection(nameof(PostgresCollection))]
public class BossGearTests(PostgresFixture postgres)
{
    private sealed record Harness(TodoDbContext Db, CombatService Combat, QuestService Quests, Guid UserId);

    private async Task<Harness> ArrangeAsync(IDiceRoller roller, int level)
    {
        await postgres.ResetAsync();
        var user = await postgres.CreateUserAsync("test|hero");

        var db = postgres.CreateContext();
        var sheets = new CharacterSheetService(db);
        var loot = new LootService(db, roller);
        var quests = new QuestService(db, loot);
        var adventurer = new AdventurerService(db, sheets, loot);
        var combat = new CombatService(db, roller, sheets, loot, quests);

        await adventurer.ChooseClassAsync(
            user.Id, ClassCatalog.Fighter, TestContext.Current.CancellationToken);

        var harness = new Harness(db, combat, quests, user.Id);
        await ReachLevelAsync(harness, level);

        var character = await db.Characters.SingleAsync(c => c.UserId == user.Id);
        character.Stamina = 40;
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
        var gamification = new GamificationService(harness.Db, new AchievementEvaluator(), harness.Quests);

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

    private async Task<Encounter> OpenAsync(Harness harness, string monsterKey, int hitPoints)
    {
        var start = await harness.Combat.StartAsync(
            harness.UserId, monsterKey, TestContext.Current.CancellationToken);

        Assert.True(start.Ok, start.Message);

        var encounter = start.Value!;
        encounter.MonsterHitPoints = hitPoints;
        await harness.Db.SaveChangesAsync();

        return encounter;
    }

    private static async Task<AttackOutcome> RoundAsync(Harness harness, Encounter encounter)
    {
        var round = await harness.Combat.AttackAsync(
            harness.UserId, encounter.Id, TestContext.Current.CancellationToken);

        Assert.True(round.Ok, round.Message);

        return round.Value!;
    }

    private static int LinesFor(IEnumerable<CombatRoll> rolls, MonsterPhase phase) =>
        rolls.Count(r => r.Text.StartsWith(phase.Line, StringComparison.Ordinal));

    /// <summary>
    /// The Elder Dragon's own regeneration carries it back over its own threshold, and it does
    /// not enter that gear a second time.
    /// </summary>
    /// <remarks>
    /// <see cref="BossPhaseTests.A_healed_boss_does_not_enter_the_same_phase_twice"/> asserts the
    /// same rule with the healing done by the test. This drives it with the mechanic that
    /// actually causes it, which matters because that mechanic is a phase entry effect: the loop
    /// being guarded against is a phase whose own entry heals the boss back over the line that
    /// triggered it, so that it re-enters, re-applies the healing, and does so every round for
    /// the rest of the fight. Nothing else in the bestiary can produce that shape, and the two
    /// halves of it live in one catalog entry.
    /// <para>
    /// The middle round is where the two numbers deliberately disagree, and both are read there.
    /// A future change that "fixed" the disagreement by assigning the derived value would pass
    /// every other test in the suite and fail this one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_boss_healed_by_its_own_gear_never_enters_that_gear_twice()
    {
        // 18 hits armour class 20 at this level and 4 on the longsword is 8 with Strength. The
        // dragon answers each round with a natural 1, which misses whatever it is carrying.
        var script = new SequenceDiceRoller(18, 4, 1, 1, 1, 18, 4, 1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller, level: 13);

        // Above the deeper threshold of 39 and already under the shallower one of 79, so one
        // blow reaches the second gear from none at all.
        var encounter = await OpenAsync(harness, MonsterCatalog.ElderDragon, hitPoints: 41);
        var dragon = encounter.Monster!;
        var lastFire = dragon.Phases![1];

        var first = await RoundAsync(harness, encounter);

        // Both gears entered, and the second one's regeneration has already ticked: 41 less 8
        // is 33, and four back is 37.
        Assert.Equal(2, first.Encounter.Phase);
        Assert.Equal(37, first.Encounter.MonsterHitPoints);
        Assert.Equal(1, LinesFor(first.Rolls, dragon.Phases[0]));
        Assert.Equal(1, LinesFor(first.Rolls, lastFire));

        // The middle round: the player misses, so the only thing that moves is the healing the
        // gear itself applied, and it carries the dragon back over its own threshold.
        var second = await RoundAsync(harness, encounter);

        Assert.Equal(41, second.Encounter.MonsterHitPoints);
        Assert.Equal(1, dragon.PhaseAt(second.Encounter.MonsterHitPoints));
        Assert.Equal(2, second.Encounter.Phase);
        Assert.Equal(0, LinesFor(second.Rolls, lastFire));

        // Back under the line, by a blow this time. The gear is not entered again.
        var third = await RoundAsync(harness, encounter);

        Assert.Equal(2, third.Encounter.Phase);
        Assert.Equal(37, third.Encounter.MonsterHitPoints);
        Assert.Equal(0, LinesFor(third.Rolls, lastFire));

        // Over the whole fight, once each. Read off the persisted log rather than off the last
        // round, because a re-entry in any round is the failure being guarded against.
        var log = CombatService.ReadLog(third.Encounter);

        Assert.Equal(1, LinesFor(log, dragon.Phases[0]));
        Assert.Equal(1, LinesFor(log, lastFire));

        // Applied once and spent three times, rather than re-applied and spent once. This is the
        // assertion the high-water mark exists for: a re-entry would put both counters back at
        // Lasting and the fight would heal four a round forever.
        var effects = StatusEffects.Read(third.Encounter);

        Assert.Equal(
            StatusEffects.Lasting - 3,
            StatusEffects.Find(effects, EffectKind.Regenerating, EffectTarget.Monster)!.Rounds);

        Assert.Equal(
            StatusEffects.Lasting - 3,
            StatusEffects.Find(effects, EffectKind.Empowered, EffectTarget.Monster)!.Rounds);

        // Three attack rolls, two damage rolls and three replies. Two gear changes, six lines of
        // narration and three regeneration ticks, and not one die between them.
        Assert.Equal(8, script.RollCount);
        Assert.Equal([20, 8, 20, 20, 20, 20, 8, 20], roller.Sides);
    }

    /// <summary>
    /// A threshold crossed by an end-of-round tick is entered on the next exchange rather than
    /// lost.
    /// </summary>
    /// <remarks>
    /// The gear check runs at one site, after the player's action, so a poison that takes a boss
    /// past a threshold at the end of round N fires the gear on round N+1. That one round of lag
    /// is a deliberate scope decision recorded on the method, and it is the sort of decision that
    /// gets quietly reversed by someone adding a second check site to "fix" a bug report. Pinned
    /// here so the reversal is a conversation rather than a surprise: what must not happen is the
    /// gear being skipped altogether, and this asserts it is not.
    /// </remarks>
    [Fact]
    public async Task A_gear_crossed_by_a_poison_tick_is_entered_on_the_next_exchange()
    {
        // Four natural 1s: two fumbled swings and two missed replies. The only thing that moves
        // the dragon's hit points is the poison.
        var script = new SequenceDiceRoller(1, 1, 1, 1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller, level: 13);

        // Three above the first threshold of 79, against a poison that takes five.
        var encounter = await OpenAsync(harness, MonsterCatalog.ElderDragon, hitPoints: 82);
        var dragon = encounter.Monster!;
        var roused = dragon.Phases![0];

        StatusEffects.Write(
            encounter,
            [new StatusEffect(EffectKind.Poisoned, EffectTarget.Monster, 2, 5, "test")]);

        await harness.Db.SaveChangesAsync();

        var first = await RoundAsync(harness, encounter);

        // The tick crossed the line after the gear check had already run for this round.
        Assert.Equal(77, first.Encounter.MonsterHitPoints);
        Assert.Equal(1, dragon.PhaseAt(first.Encounter.MonsterHitPoints));
        Assert.Equal(0, first.Encounter.Phase);
        Assert.Equal(0, LinesFor(first.Rolls, roused));

        var second = await RoundAsync(harness, encounter);

        // Read at the next exchange, not thrown away. The player's swing missed, so nothing but
        // the reading of the hit points changed between the two rounds.
        Assert.Equal(1, second.Encounter.Phase);
        Assert.Equal(1, LinesFor(second.Rolls, roused));

        Assert.NotNull(StatusEffects.Find(
            StatusEffects.Read(second.Encounter), EffectKind.Empowered, EffectTarget.Monster));

        Assert.Equal(4, script.RollCount);
        Assert.Equal([20, 20, 20, 20], roller.Sides);
    }
}
