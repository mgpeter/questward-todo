using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Services.Rpg;
using TodoApp.Data;
using TodoApp.Models.Dice;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

/// <summary>
/// Every kind of status effect, driven one at a time through a real round: it lands, it does
/// the arithmetic it says it does, it fires when it says it fires, and it stops on the round
/// its counter runs out.
/// </summary>
/// <remarks>
/// <see cref="StatusEffectTests"/> covers the engine and the two extremes, one round with every
/// kind in force at once and a round with none. Neither shape can tell which kind was read at
/// which site: a Guarded silently applied to the wrong side, or an Empowered that reached the
/// swing but not the damage, passes both. These drive the kinds separately so the site each one
/// is read at is named by a failing assertion rather than inferred.
/// <para>
/// Every script here is exact, and that is the second thing these tests are for. Only one of
/// the five kinds may change what a round costs in dice: Weakened, which buys a second d20. The
/// rest have to be free, because a kind that quietly took a die would shift every hard-coded
/// script in the suite the first time an item or a boss applied it.
/// </para>
/// <para>
/// The Fighter is armour class 14 and swings 1d8+3 at +5. The Giant Rat is armour class 10 on
/// 7 hit points at +2 for 1d4; the Skeleton is armour class 13 on 16 hit points at +4. Those
/// eight numbers are what every natural roll below is chosen against.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class EffectKindTests(PostgresFixture postgres)
{
    private sealed record Harness(TodoDbContext Db, CombatService Combat, Guid UserId);

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

        await adventurer.ChooseClassAsync(
            user.Id, ClassCatalog.Fighter, TestContext.Current.CancellationToken);

        var character = await db.Characters.SingleAsync(c => c.UserId == user.Id);
        character.Stamina = 20;
        await db.SaveChangesAsync();

        return new Harness(db, combat, user.Id);
    }

    private async Task<Encounter> OpenAsync(Harness harness, string monsterKey)
    {
        var start = await harness.Combat.StartAsync(
            harness.UserId, monsterKey, TestContext.Current.CancellationToken);

        Assert.True(start.Ok, start.Message);

        return start.Value!;
    }

    private static async Task<AttackOutcome> RoundAsync(Harness harness, Encounter encounter)
    {
        var round = await harness.Combat.AttackAsync(
            harness.UserId, encounter.Id, TestContext.Current.CancellationToken);

        Assert.True(round.Ok, round.Message);

        return round.Value!;
    }

    /// <summary>Puts effects on a fight already in progress, the way a phase or an item would.</summary>
    private static async Task ArmAsync(TodoDbContext db, Encounter encounter, params StatusEffect[] effects)
    {
        StatusEffects.Write(encounter, effects);
        await db.SaveChangesAsync();
    }

    private static async Task WoundAsync(TodoDbContext db, Encounter encounter, int hitPoints)
    {
        encounter.MonsterHitPoints = hitPoints;
        await db.SaveChangesAsync();
    }

    private static CombatRoll Swing(AttackOutcome round) =>
        round.Rolls.Single(r => r.Actor == CombatRoll.Player && r.Kind == "attack");

    private static CombatRoll Reply(AttackOutcome round) =>
        round.Rolls.Single(r => r.Actor == CombatRoll.Monster && r.Kind == "attack");

    private static CombatRoll Damage(AttackOutcome round, string actor) =>
        round.Rolls.Single(r => r.Actor == actor && r.Kind == "damage");

    private static bool Carries(CombatRoll roll, string label, int value) =>
        roll.Modifiers.Any(m => m.Label == label && m.Value == value);

    private static IEnumerable<CombatRoll> Notes(AttackOutcome round) =>
        round.Rolls.Where(r => r.Kind == "note");

    /// <summary>
    /// The other half of the named guard: the column the effects array replaced is really gone.
    /// </summary>
    /// <remarks>
    /// <see cref="StatusEffectTests.Weakened_replaces_the_old_disadvantage_column"/> asserts the
    /// mechanic still works through the new type. It cannot notice the old column still sitting
    /// on the table, and a migration that added the array without dropping the counter would
    /// leave both in place with only one of them read. Two writers of one fact is exactly the
    /// drift DEC-002 exists to refuse, and the dead one is always the one somebody trusts.
    /// <para>
    /// Asserted against the live schema rather than against the model, because the model is the
    /// thing that would have stopped mentioning it first. The property check underneath is the
    /// cheaper half of the same guard: a reintroduced property would be mapped by convention the
    /// moment it existed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_disadvantage_column_is_gone_from_the_table_and_from_the_model()
    {
        await postgres.ResetAsync();
        await using var db = postgres.CreateContext();

        var columns = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT column_name AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'encounters'
                """)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("MonsterDisadvantageRounds", columns);
        Assert.Contains("Effects", columns);

        Assert.Null(typeof(Encounter).GetProperty("MonsterDisadvantageRounds"));
    }

    /// <summary>
    /// Empowered on the player reaches both halves of a swing, and stops after exactly the
    /// number of swings it was given.
    /// </summary>
    /// <remarks>
    /// The same natural 7 hits twice and then misses. Nothing about the character or the
    /// Skeleton changes across those three rounds, so the flip is the effect running out and can
    /// be nothing else, which is a sharper statement than reading a counter back.
    /// <para>
    /// An Empowered that reached the attack roll but not the damage would pass a test that only
    /// asked whether the blow landed, so both rolls are read for the labelled modifier. Labelled
    /// rather than folded into the total on purpose: the client renders the arithmetic, and a
    /// bonus that only moved the total would be invisible in the log.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_empowered_swing_carries_the_bonus_into_the_damage_and_stops_on_time()
    {
        // 7 + 5 is 12 against armour class 13, so the swing only lands while the effect does.
        // The Skeleton answers each round with a natural 1, which always misses and therefore
        // never rolls damage.
        var script = new SequenceDiceRoller(7, 1, 1, 7, 1, 1, 7, 1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller);
        var encounter = await OpenAsync(harness, MonsterCatalog.Skeleton);

        await ArmAsync(
            harness.Db, encounter,
            new StatusEffect(EffectKind.Empowered, EffectTarget.Player, 2, 3, "test"));

        var first = await RoundAsync(harness, encounter);
        var swing = Swing(first);

        Assert.Equal("hit", swing.Outcome);
        Assert.Equal(13, swing.Target);
        Assert.Equal(15, swing.Total);
        Assert.True(Carries(swing, "empowered", 3), "the swing did not carry the bonus");

        // 1 on the longsword, +3 for Strength and +3 for the effect.
        var damage = Damage(first, CombatRoll.Player);

        Assert.Equal(7, damage.Total);
        Assert.True(Carries(damage, "empowered", 3), "the damage did not carry the bonus");
        Assert.Equal(9, first.Encounter.MonsterHitPoints);

        var second = await RoundAsync(harness, encounter);

        Assert.Equal("hit", Swing(second).Outcome);
        Assert.Equal(2, second.Encounter.MonsterHitPoints);

        // Two applications, two swings, and the second one spent the last of it.
        Assert.Equal("[]", second.Encounter.Effects);

        var third = await RoundAsync(harness, encounter);
        var wide = Swing(third);

        Assert.Equal("miss", wide.Outcome);
        Assert.Equal(12, wide.Total);
        Assert.DoesNotContain(wide.Modifiers, m => m.Label == "empowered");
        Assert.Equal(2, third.Encounter.MonsterHitPoints);

        // Three attack rolls, two damage rolls and three replies. The effect landing, being
        // read, being spent and expiring cost nothing at all.
        Assert.Equal(8, script.RollCount);
        Assert.Equal([20, 8, 20, 20, 8, 20, 20, 20], roller.Sides);
    }

    /// <summary>
    /// Empowered on the monster is read in the monster's half, and stops on time there too.
    /// </summary>
    /// <remarks>
    /// The mirror of the swing above, and worth its own test because the two sides read the
    /// array at different sites. An effect applied to the wrong target is the easiest mistake to
    /// make in an engine where both combatants share one list, and it is silent: the entry is
    /// there, the magnitude is right, and the wrong person is hitting harder.
    /// </remarks>
    [Fact]
    public async Task An_empowered_monster_hits_harder_until_the_effect_runs_out()
    {
        // The player fumbles both rounds, so the swing costs one die and no damage. The rat's
        // 8 is 10 against armour class 14 and 14 with the effect: exactly the flip.
        var script = new SequenceDiceRoller(1, 8, 2, 1, 8);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller);
        var encounter = await OpenAsync(harness, MonsterCatalog.GiantRat);

        await ArmAsync(
            harness.Db, encounter,
            new StatusEffect(EffectKind.Empowered, EffectTarget.Monster, 1, 4, "test"));

        var first = await RoundAsync(harness, encounter);
        var reply = Reply(first);

        Assert.Equal("hit", reply.Outcome);
        Assert.Equal(14, reply.Target);
        Assert.True(Carries(reply, "empowered", 4), "the reply did not carry the bonus");

        // 2 on the rat's d4 and +4 for the effect, off a Fighter's twelve.
        var damage = Damage(first, CombatRoll.Monster);

        Assert.Equal(6, damage.Total);
        Assert.True(Carries(damage, "empowered", 4), "the rat's damage did not carry the bonus");
        Assert.Equal(6, first.PlayerHitPoints);

        var second = await RoundAsync(harness, encounter);
        var wide = Reply(second);

        Assert.Equal("miss", wide.Outcome);
        Assert.Equal(10, wide.Total);
        Assert.DoesNotContain(wide.Modifiers, m => m.Label == "empowered");

        // Not a scratch in the second round, because a miss rolls no damage.
        Assert.Equal(6, second.PlayerHitPoints);

        Assert.Equal(5, script.RollCount);
        Assert.Equal([20, 20, 4, 20, 20], roller.Sides);
    }

    /// <summary>
    /// Guarded on the monster raises the number the player's swing is measured against, and
    /// gives it back when the effect runs out.
    /// </summary>
    /// <remarks>
    /// Asserted through the roll's own target rather than through whether the blow landed. The
    /// target is what the client renders and what a player reads to understand why a swing that
    /// would normally have connected did not, so a Guarded that changed the outcome without
    /// changing the reported number would be a log that lies about its own arithmetic.
    /// </remarks>
    [Fact]
    public async Task A_guarded_monster_raises_the_number_the_swing_is_measured_against()
    {
        // 6 + 5 is 11: over the rat's armour class of 10 and under the 15 the effect makes it.
        var script = new SequenceDiceRoller(6, 1, 6, 1, 1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller);
        var encounter = await OpenAsync(harness, MonsterCatalog.GiantRat);

        await ArmAsync(
            harness.Db, encounter,
            new StatusEffect(EffectKind.Guarded, EffectTarget.Monster, 1, 5, "test"));

        var first = await RoundAsync(harness, encounter);
        var guarded = Swing(first);

        Assert.Equal(15, guarded.Target);
        Assert.Equal("miss", guarded.Outcome);
        Assert.Equal(7, first.Encounter.MonsterHitPoints);

        var second = await RoundAsync(harness, encounter);
        var open = Swing(second);

        Assert.Equal(10, open.Target);
        Assert.Equal("hit", open.Outcome);

        // 1 on the longsword and +3 for Strength, off the rat's seven.
        Assert.Equal(3, second.Encounter.MonsterHitPoints);

        Assert.Equal(5, script.RollCount);
        Assert.Equal([20, 20, 20, 8, 20], roller.Sides);
    }

    /// <summary>Guarded on the player is read in the monster's half, and expires there.</summary>
    [Fact]
    public async Task A_guarded_player_is_harder_to_hit_until_the_guard_runs_out()
    {
        // The rat's 13 is 15: over a Fighter's armour class of 14 and under the 17 the guard
        // makes it. The player fumbles both rounds so the swing never rolls damage.
        var script = new SequenceDiceRoller(1, 13, 1, 13, 1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller);
        var encounter = await OpenAsync(harness, MonsterCatalog.GiantRat);

        await ArmAsync(
            harness.Db, encounter,
            new StatusEffect(EffectKind.Guarded, EffectTarget.Player, 1, 3, "test"));

        var first = await RoundAsync(harness, encounter);
        var turned = Reply(first);

        Assert.Equal(17, turned.Target);
        Assert.Equal("miss", turned.Outcome);
        Assert.Equal(12, first.PlayerHitPoints);

        var second = await RoundAsync(harness, encounter);
        var through = Reply(second);

        Assert.Equal(14, through.Target);
        Assert.Equal("hit", through.Outcome);
        Assert.Equal(11, second.PlayerHitPoints);

        Assert.Equal(5, script.RollCount);
        Assert.Equal([20, 20, 20, 20, 4], roller.Sides);
    }

    /// <summary>
    /// Poison bites once at the end of every round it is in force, and not once more.
    /// </summary>
    /// <remarks>
    /// Three identical rounds in which nothing else happens: both sides miss on a natural 1
    /// every time, so the only thing that moves the Skeleton's hit points is the tick. The third
    /// round is the point of the test. An off-by-one in the spend, in either direction, shows up
    /// there and nowhere else.
    /// </remarks>
    [Fact]
    public async Task Poison_bites_at_the_end_of_every_round_and_stops_when_its_rounds_run_out()
    {
        // Six natural 1s: three fumbled swings and three missed replies, no damage dice at all.
        var script = new SequenceDiceRoller(1, 1, 1, 1, 1, 1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller);
        var encounter = await OpenAsync(harness, MonsterCatalog.Skeleton);

        await ArmAsync(
            harness.Db, encounter,
            new StatusEffect(EffectKind.Poisoned, EffectTarget.Monster, 2, 3, "test"));

        var first = await RoundAsync(harness, encounter);

        Assert.Equal(13, first.Encounter.MonsterHitPoints);
        Assert.Contains(Notes(first), n => n.Text.StartsWith("Poison takes 3.", StringComparison.Ordinal));

        var second = await RoundAsync(harness, encounter);

        Assert.Equal(10, second.Encounter.MonsterHitPoints);
        Assert.Contains(Notes(second), n => n.Text.StartsWith("Poison takes 3.", StringComparison.Ordinal));

        var third = await RoundAsync(harness, encounter);

        // Two applications, two bites. The third round is a fight with nothing riding it.
        Assert.Equal(10, third.Encounter.MonsterHitPoints);
        Assert.DoesNotContain(Notes(third), n => n.Text.StartsWith("Poison takes", StringComparison.Ordinal));
        Assert.Equal("[]", third.Encounter.Effects);

        // Six rolls for six swings. Two ticks and their two lines in between, and not one die.
        Assert.Equal(6, script.RollCount);
        Assert.Equal([20, 20, 20, 20, 20, 20], roller.Sides);
    }

    /// <summary>
    /// Regeneration knits a wounded monster back, and stops at the maximum the catalog gives it.
    /// </summary>
    /// <remarks>
    /// The cap is the half worth writing down. A monster healed past its own maximum reports
    /// more hit points than its bestiary entry has, and every health bar in the client divides
    /// by that maximum, so the fight would render as more than full and the player would be
    /// looking at a bar past the end of its own track.
    /// </remarks>
    [Fact]
    public async Task Regeneration_knits_a_wounded_monster_back_and_stops_at_its_maximum()
    {
        var script = new SequenceDiceRoller(1, 1, 1, 1, 1, 1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller);
        var encounter = await OpenAsync(harness, MonsterCatalog.Skeleton);

        await WoundAsync(harness.Db, encounter, 5);

        await ArmAsync(
            harness.Db, encounter,
            new StatusEffect(EffectKind.Regenerating, EffectTarget.Monster, 3, 4, "test"));

        var first = await RoundAsync(harness, encounter);

        Assert.Equal(9, first.Encounter.MonsterHitPoints);

        var second = await RoundAsync(harness, encounter);

        Assert.Equal(13, second.Encounter.MonsterHitPoints);

        var third = await RoundAsync(harness, encounter);

        // Three short of the Skeleton's sixteen, so the last tick heals three of its four and
        // says three rather than claiming the whole amount.
        Assert.Equal(16, third.Encounter.MonsterHitPoints);

        Assert.Contains(
            Notes(third),
            n => n.Text.StartsWith("Skeleton knits back together for 3.", StringComparison.Ordinal));

        Assert.Equal("[]", third.Encounter.Effects);

        Assert.Equal(6, script.RollCount);
        Assert.Equal([20, 20, 20, 20, 20, 20], roller.Sides);
    }

    /// <summary>Regeneration on the player stops at the sheet, not at whatever it was given.</summary>
    /// <remarks>
    /// The same cap on the other side, and it matters more here: the character's hit points are
    /// persisted between fights, so one uncapped tick would leave the player above their own
    /// maximum until something normalised them back down.
    /// </remarks>
    [Fact]
    public async Task Regeneration_never_takes_the_player_past_the_sheet()
    {
        var script = new SequenceDiceRoller(1, 1);
        var harness = await ArrangeAsync(script);
        var encounter = await OpenAsync(harness, MonsterCatalog.Skeleton);

        var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);

        // Two short of a Fighter's twelve, against a tick that would restore nine.
        character.CurrentHitPoints = 10;
        await harness.Db.SaveChangesAsync();

        await ArmAsync(
            harness.Db, encounter,
            new StatusEffect(EffectKind.Regenerating, EffectTarget.Player, 1, 9, "test"));

        var round = await RoundAsync(harness, encounter);

        Assert.Equal(12, round.PlayerHitPoints);
        Assert.Equal(12, round.PlayerMaxHitPoints);

        Assert.Contains(
            Notes(round),
            n => n.Text.StartsWith("You knit back together for 2.", StringComparison.Ordinal));

        Assert.Equal(2, script.RollCount);
    }

    /// <summary>A tick that moves nothing writes no line.</summary>
    /// <remarks>
    /// The counter is still spent, because the effect was in force and had its turn. What is
    /// suppressed is only the line: a log someone is reading to follow a fight does not need a
    /// sentence per round announcing that nothing happened, and a regenerating monster at full
    /// health would otherwise produce one every round for the rest of the fight.
    /// </remarks>
    [Fact]
    public async Task A_tick_that_changes_nothing_writes_no_line()
    {
        var script = new SequenceDiceRoller(1, 1);
        var harness = await ArrangeAsync(script);
        var encounter = await OpenAsync(harness, MonsterCatalog.Skeleton);

        // Untouched, so there is nothing for a heal to restore.
        await ArmAsync(
            harness.Db, encounter,
            new StatusEffect(EffectKind.Regenerating, EffectTarget.Monster, 1, 4, "test"));

        var round = await RoundAsync(harness, encounter);

        Assert.Equal(16, round.Encounter.MonsterHitPoints);
        Assert.DoesNotContain(Notes(round), n => n.Text.Contains("knits back", StringComparison.Ordinal));

        // Spent all the same. The effect had its round; it simply had nothing to do with it.
        Assert.Equal("[]", round.Encounter.Effects);
    }

    /// <summary>
    /// Weakened on the player is the one effect in the game that changes what a round costs.
    /// </summary>
    /// <remarks>
    /// Two d20s while it is in force and one after, so the round genuinely consumes a different
    /// number of dice on either side of the expiry. That is why this is the only kind whose
    /// arrival can move an existing script, and why it is scripted here to the individual die
    /// rather than reasoned about: the placement of the tick, and the ruling that magnitudes are
    /// fixed at application time, exist to keep the other four free.
    /// <para>
    /// The kept die is the worse one. A disadvantage that kept the better die would be advantage
    /// wearing the wrong label, and every assertion about the outcome would still pass, so the
    /// die itself is read rather than the verdict it produced.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_weakened_player_swings_twice_and_keeps_the_worse_die_until_it_wears_off()
    {
        // 15 would land and 4 would not. While the effect holds, 4 is the one that counts.
        var script = new SequenceDiceRoller(15, 4, 1, 15, 4, 1, 15, 1, 1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller);
        var encounter = await OpenAsync(harness, MonsterCatalog.GiantRat);

        await ArmAsync(
            harness.Db, encounter,
            new StatusEffect(EffectKind.Weakened, EffectTarget.Player, 2, 0, "test"));

        var first = await RoundAsync(harness, encounter);
        var hobbled = Swing(first);

        Assert.Equal(2, hobbled.Dice.Count);
        Assert.Equal(4, hobbled.Dice.Single(d => d.Kept).Value);
        Assert.Equal(9, hobbled.Total);
        Assert.Equal("miss", hobbled.Outcome);

        var second = await RoundAsync(harness, encounter);

        Assert.Equal(2, Swing(second).Dice.Count);
        Assert.Equal(7, second.Encounter.MonsterHitPoints);

        var third = await RoundAsync(harness, encounter);
        var clear = Swing(third);

        var only = Assert.Single(clear.Dice);

        Assert.Equal(15, only.Value);
        Assert.Equal("hit", clear.Outcome);

        // The expiry is visible in the dice themselves: nine rolls where three unweakened rounds
        // of the same shape would have taken seven.
        Assert.Equal(9, script.RollCount);
        Assert.Equal([20, 20, 20, 20, 20, 20, 20, 8, 20], roller.Sides);
    }

    /// <summary>
    /// The load-bearing claim of the whole engine, stated as a comparison: arming a fight with
    /// four of the five kinds changes what happens in it and does not change one die.
    /// </summary>
    /// <remarks>
    /// The same two-round fight is fought twice against identical scripts, once bare and once
    /// with Empowered and Guarded on both sides and a poison eating the Skeleton alive. If any
    /// of those four ever reached for the roller, the two die streams would part company here,
    /// and dozens of hard-coded scripts elsewhere in the suite would quietly begin asserting
    /// something other than what they were written to assert.
    /// <para>
    /// Natural 1s throughout on purpose. A 1 always misses whatever the arithmetic says, which
    /// is what holds the two runs to the same shape while the modifiers underneath them differ:
    /// the comparison is then about what the effects cost rather than about what they did. That
    /// they did anything at all is what the last four assertions are for.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Only_disadvantage_changes_what_a_round_costs_in_dice()
    {
        int[] rolls = [1, 1, 1, 1];

        var bareScript = new SequenceDiceRoller(rolls);
        var bareRoller = new RecordingDiceRoller(bareScript);
        var bare = await ArrangeAsync(bareRoller);
        var bareFight = await OpenAsync(bare, MonsterCatalog.Skeleton);

        await RoundAsync(bare, bareFight);
        var bareEnd = await RoundAsync(bare, bareFight);

        var armedScript = new SequenceDiceRoller(rolls);
        var armedRoller = new RecordingDiceRoller(armedScript);
        var armed = await ArrangeAsync(armedRoller);
        var armedFight = await OpenAsync(armed, MonsterCatalog.Skeleton);

        await ArmAsync(
            armed.Db, armedFight,
            new StatusEffect(EffectKind.Empowered, EffectTarget.Player, 2, 5, "test"),
            new StatusEffect(EffectKind.Empowered, EffectTarget.Monster, 2, 5, "test"),
            new StatusEffect(EffectKind.Guarded, EffectTarget.Player, 2, 5, "test"),
            new StatusEffect(EffectKind.Guarded, EffectTarget.Monster, 2, 5, "test"),
            new StatusEffect(EffectKind.Poisoned, EffectTarget.Monster, 2, 3, "test"));

        await RoundAsync(armed, armedFight);
        var armedEnd = await RoundAsync(armed, armedFight);

        Assert.Equal(bareScript.RollCount, armedScript.RollCount);
        Assert.Equal(bareRoller.Sides, armedRoller.Sides);
        Assert.Equal(4, armedScript.RollCount);

        // And the effects were not merely present, they were read: four of them moved the
        // arithmetic of rolls the dice above are identical for, and six hit points came off the
        // Skeleton out of turn.
        Assert.True(Carries(Swing(armedEnd), "empowered", 5), "the swing lost its bonus");
        Assert.True(Carries(Reply(armedEnd), "empowered", 5), "the reply lost its bonus");
        Assert.Equal(18, Swing(armedEnd).Target);
        Assert.Equal(19, Reply(armedEnd).Target);

        Assert.Equal(16, bareEnd.Encounter.MonsterHitPoints);
        Assert.Equal(10, armedEnd.Encounter.MonsterHitPoints);
    }
}
