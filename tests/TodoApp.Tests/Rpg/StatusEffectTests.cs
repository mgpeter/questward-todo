using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Mapping;
using TodoApp.Api.Services.Rpg;
using TodoApp.Data;
using TodoApp.Models.Dice;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

/// <summary>
/// The status effect engine, which subsumed the Bard's MonsterDisadvantageRounds counter.
/// </summary>
/// <remarks>
/// The rules here are pure and hold with no database and no roller, so most of them are
/// asserted that way. What needs a fight is the arithmetic of the round: which effect is read
/// where, which is spent by whom, and above all how many dice the whole thing costs. Every
/// SequenceDiceRoller script in this suite hard-codes the order a round consumes its dice, and
/// the one design decision the engine rests on is that a magnitude is fixed when the effect is
/// applied and never rolled when it fires.
/// </remarks>
public class StatusEffectRuleTests
{
    private static StatusEffect Weakened(EffectTarget target, int rounds = 1) =>
        new(EffectKind.Weakened, target, rounds, 0, "test");

    [Fact]
    public void An_effect_is_found_by_its_kind_and_its_target()
    {
        List<StatusEffect> effects = [Weakened(EffectTarget.Monster)];

        Assert.NotNull(StatusEffects.Find(effects, EffectKind.Weakened, EffectTarget.Monster));

        // Same kind, other side. Both halves of a fight share one array, so the target is half
        // of the key and reading it as though it were not would weaken the wrong combatant.
        Assert.Null(StatusEffects.Find(effects, EffectKind.Weakened, EffectTarget.Player));
        Assert.Null(StatusEffects.Find(effects, EffectKind.Poisoned, EffectTarget.Monster));
    }

    /// <summary>
    /// Applying twice refreshes one entry. It never adds a second, and never adds the two
    /// magnitudes together.
    /// </summary>
    /// <remarks>
    /// This is the assignment-not-accumulation rule Vicious Mockery already had, generalised.
    /// Stacking would mean drinking five poisons for five times the damage, which is combat
    /// power minted out of nothing, of the family DEC-003 exists to refuse.
    /// </remarks>
    [Fact]
    public void Applying_the_same_effect_twice_refreshes_it_rather_than_stacking_it()
    {
        List<StatusEffect> effects = [];

        StatusEffects.Apply(effects, new StatusEffect(EffectKind.Poisoned, EffectTarget.Monster, 2, 3, "flask"));
        StatusEffects.Apply(effects, new StatusEffect(EffectKind.Poisoned, EffectTarget.Monster, 3, 4, "flask"));

        var only = Assert.Single(effects);

        Assert.Equal(3, only.Rounds);
        Assert.Equal(4, only.Magnitude);
    }

    [Fact]
    public void A_weaker_reapplication_leaves_the_stronger_effect_standing()
    {
        // Otherwise the cheapest source of an effect would be the way to cancel the dearest one.
        List<StatusEffect> effects = [];

        StatusEffects.Apply(effects, new StatusEffect(EffectKind.Guarded, EffectTarget.Player, 5, 4, "ward"));
        StatusEffects.Apply(effects, new StatusEffect(EffectKind.Guarded, EffectTarget.Player, 1, 1, "cantrip"));

        var only = Assert.Single(effects);

        Assert.Equal(5, only.Rounds);
        Assert.Equal(4, only.Magnitude);
        Assert.Equal("ward", only.Source);
    }

    [Fact]
    public void Both_sides_can_carry_the_same_kind_at_once()
    {
        List<StatusEffect> effects = [];

        StatusEffects.Apply(effects, Weakened(EffectTarget.Player));
        StatusEffects.Apply(effects, Weakened(EffectTarget.Monster));

        Assert.Equal(2, effects.Count);
    }

    [Fact]
    public void Spending_the_last_application_takes_the_effect_out_of_force()
    {
        List<StatusEffect> effects = [Weakened(EffectTarget.Monster, rounds: 2)];

        StatusEffects.Spend(effects, EffectKind.Weakened, EffectTarget.Monster);
        Assert.NotNull(StatusEffects.Find(effects, EffectKind.Weakened, EffectTarget.Monster));

        StatusEffects.Spend(effects, EffectKind.Weakened, EffectTarget.Monster);

        // Still in the array until it is pruned, but no longer in force: a caller must not be
        // able to read a magnitude out of an effect that has already done its work.
        Assert.Null(StatusEffects.Find(effects, EffectKind.Weakened, EffectTarget.Monster));
        Assert.Equal(0, StatusEffects.MagnitudeOf(effects, EffectKind.Weakened, EffectTarget.Monster));
    }

    [Fact]
    public void Spending_an_effect_that_is_not_there_does_nothing()
    {
        // Every read site spends unconditionally rather than testing first, so this has to hold.
        List<StatusEffect> effects = [];

        StatusEffects.Spend(effects, EffectKind.Weakened, EffectTarget.Monster);

        Assert.Empty(effects);
    }

    [Fact]
    public void A_spent_effect_never_reaches_the_database()
    {
        var encounter = new Encounter { MonsterKey = MonsterCatalog.Goblin };

        List<StatusEffect> effects =
        [
            Weakened(EffectTarget.Monster, rounds: 1),
            new(EffectKind.Poisoned, EffectTarget.Player, 2, 3, "flask")
        ];

        StatusEffects.Spend(effects, EffectKind.Weakened, EffectTarget.Monster);
        StatusEffects.Write(encounter, effects);

        var reloaded = StatusEffects.Read(encounter);

        Assert.Equal(EffectKind.Poisoned, Assert.Single(reloaded).Kind);
    }

    /// <summary>
    /// The exact names and numbers the AddStatusEffects backfill writes with jsonb_build_object.
    /// </summary>
    /// <remarks>
    /// The blob is serialised with no options, so property names are PascalCase and enums are
    /// numbers. Get the casing wrong in the migration and nothing throws: Read swallows
    /// JsonException by design, so a mis-cased blob binds defaults, produces a Weakened of zero
    /// rounds and is pruned away in silence. Pinning the literal here is what makes that a test
    /// failure rather than a fight that quietly lost its mechanic.
    /// </remarks>
    [Fact]
    public void The_blob_uses_the_names_and_the_numbers_the_migration_writes()
    {
        var encounter = new Encounter { MonsterKey = MonsterCatalog.Goblin };

        StatusEffects.Write(
            encounter,
            [new StatusEffect(
                EffectKind.Weakened, EffectTarget.Monster, 1, 0, ClassAbilities.ViciousMockery)]);

        Assert.Equal(
            """[{"Kind":0,"Target":1,"Rounds":1,"Magnitude":0,"Source":"vicious-mockery"}]""",
            encounter.Effects);
    }

    [Fact]
    public void Every_field_survives_a_round_trip()
    {
        var encounter = new Encounter { MonsterKey = MonsterCatalog.Goblin };

        StatusEffects.Write(
            encounter,
            [new StatusEffect(EffectKind.Regenerating, EffectTarget.Monster, 7, 4, "elder-dragon")]);

        var reloaded = Assert.Single(StatusEffects.Read(encounter));

        Assert.Equal(EffectKind.Regenerating, reloaded.Kind);
        Assert.Equal(EffectTarget.Monster, reloaded.Target);
        Assert.Equal(7, reloaded.Rounds);
        Assert.Equal(4, reloaded.Magnitude);
        Assert.Equal("elder-dragon", reloaded.Source);
    }

    /// <summary>
    /// A corrupt blob clears the afflictions rather than bricking a live fight, copying the
    /// ruling ReadUses already made for ability uses.
    /// </summary>
    /// <remarks>
    /// The trade is one-sided. Swallowing costs one fight its status effects; throwing costs
    /// that fight its playability, with no way out for the player short of a support request.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"Kind\": 0}")]
    public void A_corrupt_blob_clears_the_afflictions_rather_than_bricking_the_fight(string blob)
    {
        var encounter = new Encounter { MonsterKey = MonsterCatalog.Goblin, Effects = blob };

        Assert.Empty(StatusEffects.Read(encounter));
    }

    /// <summary>
    /// The tick fires harm before healing, spends what it fires, and costs nothing.
    /// </summary>
    /// <remarks>
    /// In the spirit of AffixAndSetTests.A_common_drop_spends_no_dice_at_all. Tick is not
    /// handed a roller and therefore cannot reach one; the empty script is here so that
    /// stopping being true is a loud failure rather than a silent shift in every dice script
    /// in the suite at once.
    /// </remarks>
    [Fact]
    public void A_full_tick_with_every_kind_in_force_spends_no_dice_at_all()
    {
        var roller = new SequenceDiceRoller();

        List<StatusEffect> effects =
        [
            Weakened(EffectTarget.Player),
            Weakened(EffectTarget.Monster),
            new(EffectKind.Empowered, EffectTarget.Player, 1, 3, "test"),
            new(EffectKind.Empowered, EffectTarget.Monster, 1, 3, "test"),
            new(EffectKind.Guarded, EffectTarget.Player, 1, 3, "test"),
            new(EffectKind.Guarded, EffectTarget.Monster, 1, 3, "test"),
            new(EffectKind.Poisoned, EffectTarget.Player, 1, 3, "test"),
            new(EffectKind.Poisoned, EffectTarget.Monster, 1, 3, "test"),
            new(EffectKind.Regenerating, EffectTarget.Player, 1, 3, "test"),
            new(EffectKind.Regenerating, EffectTarget.Monster, 1, 3, "test")
        ];

        var firing = StatusEffects.Tick(effects);

        Assert.Equal(0, roller.RollCount);

        // Only the two kinds that tick, harm before healing, each side in array order.
        string[] expected =
        [
            "Poisoned/Player", "Poisoned/Monster", "Regenerating/Player", "Regenerating/Monster"
        ];

        Assert.Equal(expected, firing.Select(e => $"{e.Kind}/{e.Target}").ToArray());

        // The magnitudes are the ones that were applied, not ones invented at fire time.
        Assert.All(firing, e => Assert.Equal(3, e.Magnitude));

        // Firing is spending: the four that fired are used up, and the six that fire somewhere
        // else in the round are untouched.
        Assert.Equal(6, StatusEffects.Read(Serialised(effects)).Count);
    }

    [Fact]
    public void The_tick_leaves_the_kinds_that_fire_elsewhere_alone()
    {
        List<StatusEffect> effects = [Weakened(EffectTarget.Monster)];

        Assert.Empty(StatusEffects.Tick(effects));
        Assert.NotNull(StatusEffects.Find(effects, EffectKind.Weakened, EffectTarget.Monster));
    }

    /// <summary>
    /// The board reaches the wire, both sides of it, in the casing the client reads.
    /// </summary>
    /// <remarks>
    /// The named guard for a whole feature shipping invisible. Every mechanic here is derived,
    /// persisted and read back correctly on the server and then rendered by one strip on the
    /// client, so a mapper that dropped the array cost nothing at compile time, broke no test,
    /// and left the player rolling at disadvantage with nothing on screen to say why.
    /// <para>
    /// The two enum-shaped fields are asserted lowercase because that is what the strip keys its
    /// icons and its colours off. PascalCase would render an unlabelled chip rather than fail.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_wire_carries_the_effects_riding_the_fight()
    {
        var encounter = Serialised(
        [
            new StatusEffect(EffectKind.Poisoned, EffectTarget.Monster, 3, 3, ItemCatalog.VialOfSerpentsKiss),
            new StatusEffect(EffectKind.Weakened, EffectTarget.Player, 2, 0, MonsterCatalog.Wyvern)
        ]);

        var effects = encounter.ToDto().Effects;

        Assert.Equal(2, effects.Count);

        var poison = Assert.Single(effects, e => e.Kind == "poisoned");

        Assert.Equal("monster", poison.Target);
        Assert.Equal(3, poison.Rounds);
        Assert.Equal(3, poison.Magnitude);
        Assert.Equal(ItemCatalog.VialOfSerpentsKiss, poison.Source);

        var weakened = Assert.Single(effects, e => e.Kind == "weakened");

        Assert.Equal("player", weakened.Target);
        Assert.Equal(2, weakened.Rounds);

        // A fight with nothing riding it sends an empty array rather than nothing at all, so the
        // client has one shape to read and the strip renders itself away on its own rule.
        Assert.Empty(new Encounter { MonsterKey = MonsterCatalog.Goblin }.ToDto().Effects);
    }

    /// <summary>An effect spent down to nothing is not in force, so it never reaches the wire.</summary>
    [Fact]
    public void A_spent_effect_is_not_reported_as_riding_the_fight()
    {
        var encounter = new Encounter { MonsterKey = MonsterCatalog.Goblin };

        // Written past the prune, the way a hand-edited row or an older writer would leave it.
        encounter.Effects =
            """[{"Kind":3,"Target":1,"Rounds":0,"Magnitude":3,"Source":"test"}]""";

        Assert.Empty(encounter.ToDto().Effects);
    }

    /// <summary>Round-trips a list through the blob, so pruning is what decides what survives.</summary>
    private static Encounter Serialised(IReadOnlyList<StatusEffect> effects)
    {
        var encounter = new Encounter { MonsterKey = MonsterCatalog.Goblin };
        StatusEffects.Write(encounter, effects);

        return encounter;
    }
}

/// <summary>
/// The engine as a fight actually runs it: which effect is read where, who spends it, and
/// exactly how many dice the whole round costs.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class StatusEffectTests(PostgresFixture postgres)
{
    private sealed record Harness(TodoDbContext Db, CombatService Combat, Guid UserId);

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
        character.Stamina = 20;
        await db.SaveChangesAsync();

        return new Harness(db, combat, user.Id);
    }

    /// <summary>Puts effects on a fight already in progress, the way a phase or an item would.</summary>
    private static async Task ArmAsync(TodoDbContext db, Encounter encounter, params StatusEffect[] effects)
    {
        StatusEffects.Write(encounter, effects);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The named guard: everything the dropped MonsterDisadvantageRounds column used to say,
    /// said through the type that replaced it.
    /// </summary>
    /// <remarks>
    /// Three facts, and they were three facts before this change too. The remark makes the
    /// monster swing at disadvantage; that swing is what consumes it; and it does not linger
    /// into the following round. Same round, same die count, same wire shape.
    /// </remarks>
    [Fact]
    public async Task Weakened_replaces_the_old_disadvantage_column()
    {
        var harness = await ArrangeAsync(
            new SequenceDiceRoller([.. Enumerable.Repeat(15, 400)]), ClassCatalog.Bard);

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Skeleton, default);

        var mock = await harness.Combat.UseAbilityAsync(
            harness.UserId, start.Value!.Id, ClassAbilities.ViciousMockery, default);

        var reply = mock.Value!.Rolls.Single(r => r.Actor == CombatRoll.Monster && r.Kind == "attack");

        // Two dice with one discarded is what disadvantage looks like on the wire.
        Assert.Equal(2, reply.Dice.Count);
        Assert.Single(reply.Dice, d => d.Kept);

        // Consumed by the counter-attack in its own round, and pruned before it was written, so
        // a spent effect reaches neither the database nor the wire.
        Assert.Null(StatusEffects.Find(
            StatusEffects.Read(mock.Value.Encounter), EffectKind.Weakened, EffectTarget.Monster));
        Assert.Equal("[]", mock.Value.Encounter.Effects);
    }

    /// <summary>
    /// A fight that was mid-mockery when the column was dropped keeps its mechanic.
    /// </summary>
    /// <remarks>
    /// The literal below is exactly what the AddStatusEffects backfill produces. Read swallows
    /// JsonException by design, so a migration that got the casing wrong would not throw: it
    /// would bind defaults, produce a Weakened of zero rounds and be pruned away in silence.
    /// This is the end-to-end half of that pin, from the blob a database holds to the shape of
    /// the swing it buys.
    /// </remarks>
    [Fact]
    public async Task A_fight_migrated_from_the_old_column_still_swings_at_disadvantage()
    {
        // 1 fumbles, so the rat lives to answer; its two disadvantage dice are the 1s after it.
        var script = new SequenceDiceRoller(1, 1, 1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller);

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);

        start.Value!.Effects =
            """[{"Kind": 0, "Target": 1, "Rounds": 1, "Magnitude": 0, "Source": "vicious-mockery"}]""";
        await harness.Db.SaveChangesAsync();

        var round = await harness.Combat.AttackAsync(harness.UserId, start.Value.Id, default);

        var reply = round.Value!.Rolls.Single(r => r.Actor == CombatRoll.Monster && r.Kind == "attack");

        Assert.Equal(2, reply.Dice.Count);
        Assert.Single(reply.Dice, d => d.Kept);

        // The source survived the conversion too, which is what keeps the Bard's own words on
        // the Bard's own effect.
        Assert.Contains("still stung by the remark", reply.Text, StringComparison.Ordinal);

        Assert.Equal(3, script.RollCount);
        Assert.Equal([20, 20, 20], roller.Sides);
    }

    /// <summary>
    /// Every kind in force on both sides at once, and the round still costs only what the
    /// attack path costs.
    /// </summary>
    /// <remarks>
    /// The exact script is the point. Two decisions bought it and both are asserted here:
    /// magnitudes are fixed when the effect is applied rather than rolled when it fires, and
    /// the tick sits at the end of the round. The only die any of this adds is the second d20
    /// a Weakened attack roll takes, which is the same second d20 Vicious Mockery already spent
    /// under the old column name.
    /// </remarks>
    [Fact]
    public async Task A_round_with_every_kind_in_force_spends_only_the_dice_the_attack_path_needs()
    {
        // Disadvantage keeps the lower die: 15 for the player, 12 for the goblin. 1 on the d8
        // leaves the goblin standing so the round has a monster half, and 3 on the d6 leaves
        // the player well clear of the floor.
        var script = new SequenceDiceRoller(15, 18, 1, 12, 16, 3);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller);

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);
        var encounter = start.Value!;

        await ArmAsync(
            harness.Db, encounter,
            new StatusEffect(EffectKind.Weakened, EffectTarget.Player, 1, 0, "test"),
            new StatusEffect(EffectKind.Weakened, EffectTarget.Monster, 1, 0, "test"),
            new StatusEffect(EffectKind.Empowered, EffectTarget.Player, 1, 2, "test"),
            new StatusEffect(EffectKind.Empowered, EffectTarget.Monster, 1, 2, "test"),
            new StatusEffect(EffectKind.Guarded, EffectTarget.Player, 1, 2, "test"),
            new StatusEffect(EffectKind.Guarded, EffectTarget.Monster, 1, 2, "test"),
            new StatusEffect(EffectKind.Poisoned, EffectTarget.Player, 1, 3, "test"),
            new StatusEffect(EffectKind.Poisoned, EffectTarget.Monster, 1, 3, "test"),
            new StatusEffect(EffectKind.Regenerating, EffectTarget.Player, 1, 2, "test"),
            new StatusEffect(EffectKind.Regenerating, EffectTarget.Monster, 1, 2, "test"));

        var round = await harness.Combat.AttackAsync(harness.UserId, encounter.Id, default);

        Assert.True(round.Ok);

        // Two d20s for a Weakened swing, one d8 of weapon damage, two d20s for the Weakened
        // answer, one d6 of its damage. The tick adds nothing.
        Assert.Equal(6, script.RollCount);
        Assert.Equal([20, 20, 8, 20, 20, 6], roller.Sides);

        var swing = round.Value!.Rolls.First(r => r.Actor == CombatRoll.Player && r.Kind == "attack");
        var reply = round.Value.Rolls.Single(r => r.Actor == CombatRoll.Monster && r.Kind == "attack");

        // Guarded raises the number the other side has to beat: goblin 12 plus 2, hero 14 plus 2.
        Assert.Equal(14, swing.Target);
        Assert.Equal(16, reply.Target);

        // Empowered is a labelled modifier on the swing and on its damage, never another die.
        Assert.Contains(swing.Modifiers, m => m.Label == "empowered" && m.Value == 2);
        Assert.Contains(
            round.Value.Rolls.First(r => r.Actor == CombatRoll.Player && r.Kind == "damage").Modifiers,
            m => m.Label == "empowered" && m.Value == 2);

        // 12 hit points, 5 from the goblin, 3 to poison, 2 back from regeneration.
        Assert.Equal(6, round.Value.PlayerHitPoints);

        // 10 hit points, 6 from the longsword, 3 to poison, 2 back from regeneration.
        Assert.Equal(3, round.Value.Encounter.MonsterHitPoints);

        Assert.Equal(EncounterStatus.Active, round.Value.Encounter.Status);

        // Every one of the ten was applied exactly once and is gone.
        Assert.Empty(StatusEffects.Read(round.Value.Encounter));
    }

    /// <summary>A poison can finish a fight the swings did not, and it pays out in full.</summary>
    /// <remarks>
    /// The victory tail moves from the middle of the round to the end of it, keeping its own
    /// dice in their own order. Skipping it because the monster died out of turn would be a
    /// kill that paid nothing.
    /// </remarks>
    [Fact]
    public async Task A_poison_kill_pays_the_same_loot_tail_a_swing_would_have()
    {
        // Player fumbles, the rat answers with its own natural 1, then the poison finishes it:
        // 3 rolls the gold on a d5 and 99 fails the rat's 15 in 100 drop chance.
        var script = new SequenceDiceRoller(1, 1, 3, 99);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller);

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);
        var encounter = start.Value!;

        await ArmAsync(
            harness.Db, encounter,
            new StatusEffect(EffectKind.Poisoned, EffectTarget.Monster, 1, 7, "flask"));

        var round = await harness.Combat.AttackAsync(harness.UserId, encounter.Id, default);

        Assert.Equal(EncounterStatus.Won, round.Value!.Encounter.Status);
        Assert.Equal(0, round.Value.Encounter.MonsterHitPoints);
        Assert.Equal(3, round.Value.GoldAwarded);

        // The loot tail's dice, in the loot tail's order, after the round's own.
        Assert.Equal(4, script.RollCount);
        Assert.Equal([20, 20, 5, 100], roller.Sides);

        // The kill was recorded where a kill by any other means would have been.
        await using var reloaded = postgres.CreateContext();
        var entry = await reloaded.BestiaryEntries.SingleAsync(
            b => b.UserId == harness.UserId && b.MonsterKey == MonsterCatalog.GiantRat);

        Assert.Equal(1, entry.Kills);
    }

    /// <summary>
    /// Damage over time never kills. It floors at one hit point and never ends the fight.
    /// </summary>
    /// <remarks>
    /// Losing in a round where nothing went wrong reads as arbitrary, and the defeat path
    /// already refuses to kill for the same reason. A poison strong enough to kill three times
    /// over is scripted here rather than a marginal one, so the floor is doing the work.
    /// </remarks>
    [Fact]
    public async Task A_tick_can_never_take_the_player_below_one_hit_point()
    {
        var script = new SequenceDiceRoller(1, 1);
        var harness = await ArrangeAsync(script);

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);
        var encounter = start.Value!;

        var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);
        character.CurrentHitPoints = 2;
        character.HitPointsUpdatedAt = DateTimeOffset.UtcNow;

        await ArmAsync(
            harness.Db, encounter,
            new StatusEffect(EffectKind.Poisoned, EffectTarget.Player, 1, 50, "flask"));

        var round = await harness.Combat.AttackAsync(harness.UserId, encounter.Id, default);

        Assert.Equal(1, round.Value!.PlayerHitPoints);
        Assert.Equal(EncounterStatus.Active, round.Value.Encounter.Status);
        Assert.Equal(2, script.RollCount);
    }

    /// <summary>
    /// Effects ride the encounter, so the fight ending is all the cleanup there is.
    /// </summary>
    /// <remarks>
    /// The reason they live here rather than on the character: nothing has to remember to clear
    /// them, and nothing can leak into the next fight by being forgotten.
    /// </remarks>
    [Fact]
    public async Task An_effect_dies_with_the_fight_it_was_applied_in()
    {
        var harness = await ArrangeAsync(new SequenceDiceRoller([.. Enumerable.Repeat(1, 100)]));

        var first = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);
        var encounter = first.Value!;

        await ArmAsync(
            harness.Db, encounter,
            new StatusEffect(EffectKind.Weakened, EffectTarget.Monster, StatusEffects.Lasting, 0, "test"));

        await harness.Combat.FleeAsync(harness.UserId, encounter.Id, default);

        var second = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);

        Assert.True(second.Ok);
        Assert.Empty(StatusEffects.Read(second.Value!));
    }
}
