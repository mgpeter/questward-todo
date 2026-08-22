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
/// The phase arithmetic, which is pure and needs neither a database nor a die.
/// </summary>
public class MonsterPhaseRuleTests
{
    /// <summary>
    /// The rule has to mean the same thing at both ends of the bestiary.
    /// </summary>
    /// <remarks>
    /// The obvious implementation divides into a percentage first, and integer division makes
    /// that wrong in opposite directions at the two ends: a 7 hit point monster reads 6 hit
    /// points as 0 percent and enters its phase on the first scratch, while a 132 hit point
    /// dragon rounds a threshold the other way. Cross-multiplying keeps one rule.
    /// </remarks>
    [Fact]
    public void PhaseAt_reads_the_same_at_both_ends_of_the_bestiary()
    {
        // The small end, at the Giant Rat's seven hit points. Built here rather than given to
        // the rat itself, because the whole schedule of this phase is that the monsters the
        // exact-script tests fight do not gain gears.
        var small = new MonsterDefinition(
            "test-vermin", "Test Vermin", "For the arithmetic.",
            Level: 1, ArmourClass: 10, MaxHitPoints: 7, AttackBonus: 2, DamageNotation: "1d4",
            MinGold: 1, MaxGold: 2, DropChance: 0,
            [new LootEntry(ItemCatalog.WornDagger, 1)],
            [new MonsterPhase(50, "Cornered", "It turns.", [])]);

        // Six of seven is not half of anything. Dividing first would read it as zero percent.
        Assert.Equal(0, small.PhaseAt(6));
        Assert.Equal(0, small.PhaseAt(4));

        // Half of seven is three and a half, so three is under it and four is not.
        Assert.Equal(1, small.PhaseAt(3));
        Assert.Equal(1, small.PhaseAt(0));

        var dragon = MonsterCatalog.Find(MonsterCatalog.ElderDragon)!;

        // 132 hit points, thresholds at 60 and 30 percent: 79.2 and 39.6.
        Assert.Equal(0, dragon.PhaseAt(132));
        Assert.Equal(0, dragon.PhaseAt(80));
        Assert.Equal(1, dragon.PhaseAt(79));
        Assert.Equal(1, dragon.PhaseAt(40));
        Assert.Equal(2, dragon.PhaseAt(39));
        Assert.Equal(2, dragon.PhaseAt(1));
    }

    [Fact]
    public void A_monster_with_no_phases_is_never_in_one()
    {
        var rat = MonsterCatalog.Find(MonsterCatalog.GiantRat)!;

        Assert.Null(rat.Phases);
        Assert.Equal(0, rat.PhaseAt(7));
        Assert.Equal(0, rat.PhaseAt(1));
        Assert.Null(rat.PhaseDefinition(1));
    }

    [Fact]
    public void A_phase_number_outside_the_catalog_reads_as_no_phase()
    {
        var dragon = MonsterCatalog.Find(MonsterCatalog.ElderDragon)!;

        // Zero is the ordinary "no phase entered", and the two either side of the range are
        // what a hand-edited row or a retuned catalog would leave behind.
        Assert.Null(dragon.PhaseDefinition(0));
        Assert.Null(dragon.PhaseDefinition(3));
        Assert.Null(dragon.PhaseDefinition(-1));

        Assert.Equal("Roused", dragon.PhaseDefinition(1)!.Name);
        Assert.Equal("Last Fire", dragon.PhaseDefinition(2)!.Name);
    }

    /// <summary>The wire says which phase, and names it from the catalog rather than storing it.</summary>
    [Fact]
    public void The_wire_names_the_phase_from_the_stored_number()
    {
        var encounter = new Encounter
        {
            MonsterKey = MonsterCatalog.ElderDragon,
            MonsterHitPoints = 40,
            Phase = 1
        };

        var entered = encounter.ToDto();

        Assert.Equal(1, entered.Phase);
        Assert.Equal("Roused", entered.PhaseName);

        // An ordinary fight against something with no gears reports the same shape, so the
        // client has one field to read rather than a special case.
        var plain = new Encounter { MonsterKey = MonsterCatalog.GiantRat, MonsterHitPoints = 7 }.ToDto();

        Assert.Equal(0, plain.Phase);
        Assert.Null(plain.PhaseName);
    }
}

/// <summary>
/// What a consumable is, before any of it reaches a database.
/// </summary>
public class ConsumableRuleTests
{
    [Fact]
    public void Rarity_buys_magnitude_and_leaves_duration_alone()
    {
        var poison = ItemCatalog.Find(ItemCatalog.VialOfSerpentsKiss)!.Use!;

        Assert.Equal(3, poison.At(Rarity.Common).Magnitude);
        Assert.Equal(5, poison.At(Rarity.Rare).Magnitude);
        Assert.Equal(7, poison.At(Rarity.Legendary).Magnitude);

        // Duration deliberately does not scale. A Legendary poison hits harder, it does not
        // hold the monster in place for twice as long.
        Assert.All(
            Enum.GetValues<Rarity>(),
            r => Assert.Equal(poison.Rounds, poison.At(r).Rounds));
    }

    [Fact]
    public void A_healing_draught_gets_better_with_rarity_too()
    {
        // Otherwise a Rare Draught of Mending would cost eight times a Common one and do
        // exactly the same thing, and the upgrade bench would be selling nothing.
        var mending = ItemCatalog.Find(ItemCatalog.DraughtOfMending)!.Use!;

        Assert.Equal(8, mending.At(Rarity.Common).Heal);
        Assert.Equal(10, mending.At(Rarity.Rare).Heal);
    }

    /// <summary>
    /// A zero stays a zero. Rarity may not invent a heal or a magnitude that the catalog did
    /// not give the item.
    /// </summary>
    /// <remarks>
    /// Without the guard a Rare Smoke Pellet would start healing two hit points, and a Weakened
    /// effect, whose magnitude is meaningless by design, would start carrying a number that
    /// nothing reads and every log line would report.
    /// </remarks>
    [Fact]
    public void A_rarity_bonus_never_invents_a_heal_or_a_magnitude()
    {
        var pellet = ItemCatalog.Find(ItemCatalog.SmokePellet)!.Use!;

        Assert.All(Enum.GetValues<Rarity>(), rarity =>
        {
            Assert.Equal(0, pellet.At(rarity).Heal);
            Assert.Equal(0, pellet.At(rarity).Magnitude);
        });

        var mending = ItemCatalog.Find(ItemCatalog.DraughtOfMending)!.Use!;

        Assert.All(Enum.GetValues<Rarity>(), rarity => Assert.Equal(0, mending.At(rarity).Magnitude));
    }

    /// <summary>
    /// The stacking key is (UserId, ItemKey, Rarity) and nothing else, so anything a consumable
    /// could carry outside that key would be silently merged away by the upsert.
    /// </summary>
    /// <remarks>
    /// Affixes are the only such thing, and this is the proof that two consumables cannot differ
    /// by one: no slot to roll into at any rarity, no pool to roll from, and no die spent trying.
    /// The forge's imbue bench is held to the same ruling by ForgeServiceTests, so there is no
    /// route that could put a word on one after the fact either.
    /// </remarks>
    [Fact]
    public void Two_consumables_that_share_the_stack_key_can_never_differ_by_an_affix()
    {
        var roller = new SequenceDiceRoller();

        Assert.All(Enum.GetValues<Rarity>(), rarity =>
        {
            Assert.Equal(0, AffixRules.RollableFor(ItemSlot.Consumable, rarity));
            Assert.Empty(AffixRules.EligibleFor(ItemSlot.Consumable, rarity));

            var (prefix, suffix) = AffixRules.Roll(ItemSlot.Consumable, rarity, roller);

            Assert.Null(prefix);
            Assert.Null(suffix);
        });

        Assert.Equal(0, roller.RollCount);

        // So the name on a merged row is the plain catalog name at every rarity, and no screen
        // could have told the two rows apart in the first place.
        var stack = new InventoryItem
        {
            ItemKey = ItemCatalog.DraughtOfMending,
            Slot = ItemSlot.Consumable,
            Rarity = Rarity.Legendary
        };

        Assert.Equal("Draught of Mending", stack.DisplayName);
    }

    /// <summary>
    /// The re-draw that enforces the one-consumable rule always has something left to draw.
    /// </summary>
    /// <remarks>
    /// It is a rejection loop rather than a filtered pool, so it terminates only because an
    /// acceptable draw is always available. Six slots against eighty-odd pieces of gear is not
    /// close, but the loop would hang rather than fail if that ever stopped being true, and a
    /// hanging test says nothing about why.
    /// </remarks>
    [Fact]
    public void The_shelf_always_has_something_left_to_draw()
    {
        var wearable = ItemCatalog.All.Count(i => i.Slot != ItemSlot.Consumable);

        Assert.True(
            wearable > ShopService.OfferCount,
            $"{wearable} non-consumables against {ShopService.OfferCount} slots leaves no room to re-draw");
    }

    /// <summary>
    /// At most one of the six slots, over four months of shelves for two shoppers.
    /// </summary>
    /// <remarks>
    /// A shelf is six slots and the shop is the only priced route to gear. Two potions on it is
    /// a third of the day's gear gone, and six would be a day with none at all.
    /// </remarks>
    [Fact]
    public void A_shelf_never_shows_two_consumables()
    {
        var monday = new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

        Guid[] shoppers =
        [
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222")
        ];

        var sawOne = false;

        foreach (var shopper in shoppers)
        {
            for (var day = 0; day < 120; day++)
            {
                var stock = ShopService.StockFor(shopper, monday.AddDays(day));
                var consumables = stock.Offers.Count(o => o.Item.Slot == ItemSlot.Consumable);

                Assert.True(consumables <= 1, $"day {day}: {consumables} consumables on one shelf");

                // The re-draw must not have terminated the loop early either.
                Assert.Equal(ShopService.OfferCount, stock.Offers.Count);

                sawOne |= consumables == 1;
            }
        }

        // A rule that never fires is a rule nobody is testing. Consumables have to reach the
        // shelf at all for the cap above to mean anything.
        Assert.True(sawOne, "no shelf in 240 offered a consumable, so the cap proves nothing");
    }

    /// <summary>
    /// The slot is reserved, not merely capped: every shelf carries exactly one consumable.
    /// </summary>
    /// <remarks>
    /// The cap above and this are opposite guarantees and only one of them is what task 5.4
    /// asked for. Rejecting a second consumable out of one shared pool caps the count at one but
    /// promises nothing at all: five consumables among eighty-one items left two shelves in three
    /// carrying none, and the shop is the only route to one in the game, so the whole consumable
    /// system was inert for a player who had never happened onto a lucky shelf.
    /// <para>
    /// Also asserted: the reserved slot moves. A potion pinned to the last slot every day would
    /// satisfy the count and still read as an afterthought rather than as stock.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_shelf_reserves_exactly_one_slot_for_a_consumable()
    {
        var monday = new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

        Guid[] shoppers =
        [
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222")
        ];

        var positions = new HashSet<int>();

        foreach (var shopper in shoppers)
        {
            for (var day = 0; day < 120; day++)
            {
                var stock = ShopService.StockFor(shopper, monday.AddDays(day));

                var potionSlots = stock.Offers
                    .Select((offer, slot) => (offer, slot))
                    .Where(o => o.offer.Item.Slot == ItemSlot.Consumable)
                    .Select(o => o.slot)
                    .ToList();

                Assert.Equal(ShopService.OfferCount, stock.Offers.Count);

                Assert.True(
                    potionSlots.Count == 1,
                    $"day {day}: {potionSlots.Count} consumables on one shelf");

                positions.Add(potionSlots[0]);
            }
        }

        Assert.True(
            positions.Count > 1,
            $"the potion sat in the same slot every day: {string.Join(", ", positions)}");
    }
}

/// <summary>
/// Boss phases as a fight actually runs them: which gear is entered, when, and at what cost
/// in dice.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class BossPhaseTests(PostgresFixture postgres)
{
    private sealed record Harness(TodoDbContext Db, CombatService Combat, QuestService Quests, Guid UserId);

    private async Task<Harness> ArrangeAsync(IDiceRoller roller, int level)
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

    /// <summary>Puts a fight in progress on a chosen number of hit points.</summary>
    private static async Task WoundAsync(TodoDbContext db, Encounter encounter, int hitPoints)
    {
        encounter.MonsterHitPoints = hitPoints;
        await db.SaveChangesAsync();
    }

    private static int PhaseLines(IEnumerable<CombatRoll> rolls, MonsterPhase phase) =>
        rolls.Count(r => r.Text.StartsWith(phase.Line, StringComparison.Ordinal));

    /// <summary>
    /// One blow past two thresholds enters both gears, not only the deeper one.
    /// </summary>
    /// <remarks>
    /// Skipping the middle phase would make a big hit the way to dodge the mechanic it was
    /// supposed to trigger, which is the opposite of what a threshold is for. The fight is
    /// arranged mid-way rather than fought down from full health, because no weapon in the game
    /// deals the forty points between the Elder Dragon's two thresholds in one swing.
    /// </remarks>
    [Fact]
    public async Task A_single_blow_that_crosses_two_thresholds_applies_both()
    {
        // 18 hits AC 20 at this level, 4 on the longsword takes 41 to 33, and the dragon's
        // answering 1 misses. Nothing about a phase change asks for a die.
        var script = new SequenceDiceRoller(18, 4, 1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller, level: 13);

        var start = await harness.Combat.StartAsync(
            harness.UserId, MonsterCatalog.ElderDragon, TestContext.Current.CancellationToken);

        var encounter = start.Value!;
        var dragon = encounter.Monster!;

        // Above the deeper threshold of 39 and already under the shallower one of 79, with the
        // stored phase still zero: one blow therefore reaches phase two from phase nought.
        await WoundAsync(harness.Db, encounter, 41);

        var round = await harness.Combat.AttackAsync(
            harness.UserId, encounter.Id, TestContext.Current.CancellationToken);

        Assert.True(round.Ok);
        Assert.Equal(2, round.Value!.Encounter.Phase);

        Assert.Equal(1, PhaseLines(round.Value.Rolls, dragon.Phases![0]));
        Assert.Equal(1, PhaseLines(round.Value.Rolls, dragon.Phases[1]));

        // Both gears' effects are on the board. The second phase's Empowered replaced the
        // first's rather than stacking with it, which is the refresh rule doing its work.
        var effects = StatusEffects.Read(round.Value.Encounter);

        Assert.NotNull(StatusEffects.Find(effects, EffectKind.Regenerating, EffectTarget.Monster));
        Assert.Equal(3, StatusEffects.MagnitudeOf(effects, EffectKind.Empowered, EffectTarget.Monster));

        // And it was in force for the answer it provoked, in this same round.
        var reply = round.Value.Rolls.Single(r => r.Actor == CombatRoll.Monster && r.Kind == "attack");

        Assert.Contains(reply.Modifiers, m => m.Label == "empowered" && m.Value == 3);

        // The player's d20, the longsword's d8, the dragon's d20. Two phase changes, four log
        // lines and a regeneration tick between them, and not one die.
        Assert.Equal(3, script.RollCount);
        Assert.Equal([20, 8, 20], roller.Sides);
    }

    /// <summary>
    /// A phase change is a live flavour moment, and the line it prints comes from the catalog.
    /// </summary>
    /// <remarks>
    /// The named guard for FlavourMoment.PhaseChange being emitted. Its doc comment said it was
    /// reserved and not yet emitted long after ResolvePhaseChange started calling it, which
    /// invited exactly one edit: delete the member and its lines as dead weight. That is not a
    /// quiet edit. FlavourCatalog.Pick indexes Lines with an unguarded dictionary indexer, so
    /// every boss phase change would throw, and renumbering the enum instead would slide
    /// EffectApplied onto this ordinal and re-pick the line for every future moment of that kind.
    /// <para>
    /// The exact line is asserted rather than merely that one exists, because Pick folds the
    /// moment's ordinal into its hash: a renumbered member still produces a sentence, just a
    /// different one, and only comparing against the moment by name catches that.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_phase_change_prints_a_line_from_the_phase_change_moment()
    {
        // 18 hits, 4 on the longsword takes 41 to 33, and the dragon's answering 1 misses.
        var script = new SequenceDiceRoller(18, 4, 1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller, level: 13);

        var start = await harness.Combat.StartAsync(
            harness.UserId, MonsterCatalog.ElderDragon, TestContext.Current.CancellationToken);

        var encounter = start.Value!;
        var dragon = encounter.Monster!;

        await WoundAsync(harness.Db, encounter, 41);

        var round = await harness.Combat.AttackAsync(
            harness.UserId, encounter.Id, TestContext.Current.CancellationToken);

        var line = Assert.Single(
            round.Value!.Rolls,
            r => r.Text.StartsWith(dragon.Phases![1].Line, StringComparison.Ordinal));

        var expected = FlavourCatalog.Pick(
            FlavourMoment.PhaseChange, encounter.Id, round.Value.Encounter.Round, dragon.Name);

        Assert.Equal(expected, line.Flavour);

        // And it came out of this moment's own pool, not out of whichever pool the ordinal
        // happens to point at.
        Assert.Contains(
            expected,
            FlavourCatalog.For(FlavourMoment.PhaseChange)
                .Select(l => l.Replace(FlavourCatalog.MonsterToken, dragon.Name, StringComparison.Ordinal)));

        // Narration still costs nothing. Two phase changes and their lines, and the same three
        // dice the fight would have spent with no gears at all.
        Assert.Equal(3, script.RollCount);
        Assert.Equal([20, 8, 20], roller.Sides);
    }

    /// <summary>
    /// The named guard for the intentional disagreement between the stored phase and the phase
    /// the hit points read as.
    /// </summary>
    /// <remarks>
    /// Encounter.Phase is a high-water mark. Without it, a boss healed back over its own
    /// threshold re-enters on the next blow that crosses it and re-applies its entry effects,
    /// every round, for the rest of the fight. The Rounds assertion below is what makes the
    /// difference visible: an effect re-applied would be back at Lasting and then spent once,
    /// where one applied a single time has been spent by two answering swings.
    /// </remarks>
    [Fact]
    public async Task A_healed_boss_does_not_enter_the_same_phase_twice()
    {
        // Two identical rounds: 18 hits AC 18, 4 on the longsword, and the dragon's 1 misses.
        var script = new SequenceDiceRoller(18, 4, 1, 18, 4, 1);
        var harness = await ArrangeAsync(script, level: 10);

        var start = await harness.Combat.StartAsync(
            harness.UserId, MonsterCatalog.YoungDragon, TestContext.Current.CancellationToken);

        var encounter = start.Value!;
        var phase = encounter.Monster!.Phases![0];

        // 48 of 95 is a whisker above the halfway threshold of 47.
        await WoundAsync(harness.Db, encounter, 48);

        var first = await harness.Combat.AttackAsync(
            harness.UserId, encounter.Id, TestContext.Current.CancellationToken);

        Assert.Equal(1, first.Value!.Encounter.Phase);
        Assert.Equal(1, PhaseLines(first.Value.Rolls, phase));

        // Healed back over its own threshold, by any means. The phase it entered stays entered.
        await WoundAsync(harness.Db, encounter, 55);

        Assert.Equal(1, encounter.Phase);

        var second = await harness.Combat.AttackAsync(
            harness.UserId, encounter.Id, TestContext.Current.CancellationToken);

        // Back under the threshold, and nothing happens.
        Assert.Equal(47, second.Value!.Encounter.MonsterHitPoints);
        Assert.Equal(1, second.Value.Encounter.Phase);
        Assert.DoesNotContain(
            second.Value.Rolls, r => r.Text.StartsWith(phase.Line, StringComparison.Ordinal));

        // Applied once and spent by two answering swings, rather than applied twice and spent
        // by two. This is the assertion the whole high-water mark exists for.
        var empowered = StatusEffects.Find(
            StatusEffects.Read(second.Value.Encounter), EffectKind.Empowered, EffectTarget.Monster);

        Assert.NotNull(empowered);
        Assert.Equal(StatusEffects.Lasting - 2, empowered.Rounds);
    }

    /// <summary>
    /// The Wyvern's gear is the one place a round's roll count grows without the player having
    /// chosen it, and it is the first thing in the game that makes the player roll at
    /// disadvantage.
    /// </summary>
    /// <remarks>
    /// Called out with a scripted die count rather than left to be discovered, because it is
    /// exactly the kind of change that silently rewrites what an existing script asserts.
    /// </remarks>
    [Fact]
    public async Task The_wyverns_gear_makes_the_player_swing_at_disadvantage()
    {
        // Round one: 18 hits AC 19, 4 on the longsword takes 60 to 52, the Wyvern's 1 misses.
        // Round two: two d20s for the player because Weakened is now on them, the lower kept,
        // and 3 plus the bonus is nowhere near AC 19, so there is no damage roll. The Wyvern
        // misses again.
        var script = new SequenceDiceRoller(18, 4, 1, 18, 3, 1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller, level: 12);

        var start = await harness.Combat.StartAsync(
            harness.UserId, MonsterCatalog.Wyvern, TestContext.Current.CancellationToken);

        var encounter = start.Value!;

        // 60 of 118 is one point above the halfway threshold of 59.
        await WoundAsync(harness.Db, encounter, 60);

        var first = await harness.Combat.AttackAsync(
            harness.UserId, encounter.Id, TestContext.Current.CancellationToken);

        Assert.Equal(1, first.Value!.Encounter.Phase);

        // The player's own swing is at disadvantage, and it lasts three of them.
        var weakened = StatusEffects.Find(
            StatusEffects.Read(first.Value.Encounter), EffectKind.Weakened, EffectTarget.Player);

        Assert.NotNull(weakened);
        Assert.Equal(3, weakened.Rounds);

        var second = await harness.Combat.AttackAsync(
            harness.UserId, encounter.Id, TestContext.Current.CancellationToken);

        var swing = second.Value!.Rolls.Single(r => r.Actor == CombatRoll.Player && r.Kind == "attack");

        Assert.Equal(2, swing.Dice.Count);
        Assert.Single(swing.Dice, d => d.Kept);

        // Three dice in the first round and three in the second, and the extra one is the
        // second d20 of the disadvantaged swing rather than anything the phase itself spent.
        Assert.Equal(6, script.RollCount);
        Assert.Equal([20, 8, 20, 20, 20, 20], roller.Sides);
    }

    /// <summary>
    /// A phase entered by the blow that provoked it is in force for the answer to that blow.
    /// </summary>
    /// <remarks>
    /// The alternative placement, after the monster's half, would make every gear a round late
    /// and the log would read as the boss announcing a change and then not making it.
    /// </remarks>
    [Fact]
    public async Task A_gear_entered_this_round_is_already_in_force_for_the_answer()
    {
        // 18 hits AC 19, 4 on the longsword takes 64 to 56, and the Basilisk's 1 misses.
        var script = new SequenceDiceRoller(18, 4, 1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller, level: 12);

        var start = await harness.Combat.StartAsync(
            harness.UserId, MonsterCatalog.Basilisk, TestContext.Current.CancellationToken);

        var encounter = start.Value!;

        // 64 of 105 is one point above the sixty percent threshold of 63.
        await WoundAsync(harness.Db, encounter, 64);

        var round = await harness.Combat.AttackAsync(
            harness.UserId, encounter.Id, TestContext.Current.CancellationToken);

        Assert.Equal(1, round.Value!.Encounter.Phase);

        // Guarded on the monster is read by the player's swing, so it starts mattering next
        // round rather than retroactively: this round's swing was measured against the plain
        // armour class, and the effect is still standing to be read.
        var swing = round.Value.Rolls.Single(r => r.Actor == CombatRoll.Player && r.Kind == "attack");

        Assert.Equal(19, swing.Target);

        Assert.Equal(2, StatusEffects.MagnitudeOf(
            StatusEffects.Read(round.Value.Encounter), EffectKind.Guarded, EffectTarget.Monster));

        // The player's d20, the longsword's d8, the Basilisk's d20. Entering a gear is free.
        Assert.Equal(3, script.RollCount);
        Assert.Equal([20, 8, 20], roller.Sides);
    }

    /// <summary>
    /// Nothing is appended to the opening log, because a monster opens on full hit points.
    /// </summary>
    /// <remarks>
    /// This is why the check does not run at StartAsync. FlavourTests asserts that a fight opens
    /// having spent no dice and that the last opening line is the opening flavour, and a phase
    /// note at the open would break the second of those without touching a die.
    /// </remarks>
    [Fact]
    public async Task A_boss_enters_no_phase_at_the_moment_the_fight_opens()
    {
        var roller = new RecordingDiceRoller(new SequenceDiceRoller());
        var harness = await ArrangeAsync(roller, level: 13);

        var start = await harness.Combat.StartAsync(
            harness.UserId, MonsterCatalog.ElderDragon, TestContext.Current.CancellationToken);

        Assert.True(start.Ok);
        Assert.Equal(0, start.Value!.Phase);
        Assert.Equal(0, start.Value.Monster!.PhaseAt(start.Value.MonsterHitPoints));
        Assert.Empty(roller.Sides);

        // Two lines, the mechanical opening and its flavour, exactly as before.
        Assert.Equal(2, CombatService.ReadLog(start.Value).Count);
    }
}

/// <summary>
/// Consumables: how they arrive, how they stack, what spending one costs, and what it buys.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class ConsumableTests(PostgresFixture postgres)
{
    private sealed record Harness(
        TodoDbContext Db,
        CombatService Combat,
        AdventurerService Adventurer,
        LootService Loot,
        ShopService Shop,
        ForgeService Forge,
        Guid UserId);

    private async Task<Harness> ArrangeAsync(IDiceRoller roller, Guid? userId = null)
    {
        await postgres.ResetAsync();
        var id = userId ?? (await postgres.CreateUserAsync("test|hero")).Id;

        if (userId is not null)
        {
            await CreateUserWithIdAsync(id);
        }

        var db = postgres.CreateContext();
        var sheets = new CharacterSheetService(db);
        var loot = new LootService(db, roller);
        var quests = new QuestService(db, loot, new ChronicleService(db));
        var adventurer = new AdventurerService(db, sheets, loot);
        var combat = new CombatService(db, roller, sheets, loot, quests, new ChronicleService(db));

        await adventurer.ChooseClassAsync(id, ClassCatalog.Fighter, TestContext.Current.CancellationToken);

        var character = await db.Characters.SingleAsync(c => c.UserId == id);
        character.Stamina = 40;
        character.Gold = 100_000;
        await db.SaveChangesAsync();

        return new Harness(
            db, combat, adventurer, loot, new ShopService(db), new ForgeService(db, roller), id);
    }

    /// <summary>
    /// A user on a chosen id, because the shelf is a pure function of the id and the date and
    /// the buy path takes the date from the clock.
    /// </summary>
    private async Task CreateUserWithIdAsync(Guid id)
    {
        await using var db = postgres.CreateContext();

        db.Users.Add(new User { Id = id, Auth0Sub = $"test|{id}" });
        db.Characters.Add(new Character { UserId = id });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The first id whose shelf today holds a consumable, so the purchase leg of the stacking
    /// guard can actually buy one.
    /// </summary>
    private static (Guid Shopper, ShopOffer Offer) ShopperWithAConsumableToday(DateTimeOffset now)
    {
        for (var seed = 1; seed < 500; seed++)
        {
            var candidate = new Guid(seed, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]);

            var offer = ShopService.StockFor(candidate, now).Offers
                .FirstOrDefault(o => o.Item.Slot == ItemSlot.Consumable);

            if (offer is not null)
            {
                return (candidate, offer);
            }
        }

        throw new InvalidOperationException("No shelf in five hundred offered a consumable today.");
    }

    private async Task<InventoryItem> StackAsync(Harness harness, string itemKey, Rarity rarity, int quantity)
    {
        var item = await harness.Loot.GrantAsync(
            harness.UserId, itemKey, rarity, TestContext.Current.CancellationToken);

        item.Quantity = quantity;
        await harness.Db.SaveChangesAsync();

        return item;
    }

    private async Task<InventoryItem?> ReloadAsync(Guid itemId)
    {
        await using var db = postgres.CreateContext();

        return await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == itemId);
    }

    /// <summary>
    /// The single most likely bug in this work, pinned: both acquisition paths upsert onto one
    /// row rather than inserting beside it.
    /// </summary>
    /// <remarks>
    /// The shop and the loot service both used to add rows directly. Against the stacking index
    /// the second acquisition of the same potion at the same rarity is a constraint violation,
    /// which in the shop's case is a 500 with the gold already taken. Both legs are exercised
    /// here, and in one unit of work, because a helper that only consulted the database would
    /// miss a row added earlier in the same SaveChanges.
    /// </remarks>
    [Fact]
    public async Task Buying_and_granting_the_same_potion_leaves_one_row_with_a_count()
    {
        var now = DateTimeOffset.UtcNow;
        var (shopper, offer) = ShopperWithAConsumableToday(now);
        var harness = await ArrangeAsync(new FixedDiceRoller(1), shopper);

        await harness.Loot.GrantAsync(
            harness.UserId, offer.Item.Key, offer.Rarity, TestContext.Current.CancellationToken);

        var bought = await harness.Shop.BuyAsync(
            harness.UserId, offer.OfferId, TestContext.Current.CancellationToken);

        Assert.True(bought.Ok);

        await harness.Loot.GrantAsync(
            harness.UserId, offer.Item.Key, offer.Rarity, TestContext.Current.CancellationToken);

        await harness.Db.SaveChangesAsync();

        await using var reloaded = postgres.CreateContext();

        var rows = await reloaded.InventoryItems
            .Where(i => i.UserId == harness.UserId && i.ItemKey == offer.Item.Key)
            .ToListAsync(TestContext.Current.CancellationToken);

        var stack = Assert.Single(rows);

        Assert.Equal(3, stack.Quantity);
        Assert.Equal(offer.Rarity, stack.Rarity);
        Assert.Equal(ItemSlot.Consumable, stack.Slot);
    }

    /// <summary>The same key at another rarity is a different item and gets its own row.</summary>
    [Fact]
    public async Task A_second_rarity_of_the_same_potion_is_its_own_stack()
    {
        var harness = await ArrangeAsync(new FixedDiceRoller(1));

        await harness.Loot.GrantAsync(
            harness.UserId, ItemCatalog.WhetstoneOil, Rarity.Common, TestContext.Current.CancellationToken);
        await harness.Loot.GrantAsync(
            harness.UserId, ItemCatalog.WhetstoneOil, Rarity.Rare, TestContext.Current.CancellationToken);
        await harness.Db.SaveChangesAsync();

        await using var reloaded = postgres.CreateContext();

        var rows = await reloaded.InventoryItems
            .Where(i => i.UserId == harness.UserId && i.ItemKey == ItemCatalog.WhetstoneOil)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(1, r.Quantity));
    }

    /// <summary>Gear does not stack. A backpack of five identical swords is still five rows.</summary>
    [Fact]
    public async Task Worn_gear_still_gets_a_row_of_its_own_every_time()
    {
        var harness = await ArrangeAsync(new FixedDiceRoller(1));

        for (var i = 0; i < 3; i++)
        {
            await harness.Loot.GrantAsync(
                harness.UserId, ItemCatalog.GreatAxe, Rarity.Common, TestContext.Current.CancellationToken);
        }

        await harness.Db.SaveChangesAsync();

        await using var reloaded = postgres.CreateContext();

        var rows = await reloaded.InventoryItems
            .Where(i => i.UserId == harness.UserId && i.ItemKey == ItemCatalog.GreatAxe)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(1, r.Quantity));
    }

    /// <summary>
    /// Drinking one takes the player's half of the round, heals a known number, and spends no
    /// die at all.
    /// </summary>
    /// <remarks>
    /// The monster still answers, which is the whole price. Healing that cost no turn would let
    /// any losing fight be won without opening another one, and one unit of stamina buying two
    /// fights' worth of survival is inflation of the DEC-003 family wearing a different hat.
    /// </remarks>
    [Fact]
    public async Task Drinking_a_draught_heals_takes_the_round_and_spends_no_dice()
    {
        // The only die in the round is the rat's answering swing, and its 1 misses.
        var script = new SequenceDiceRoller(1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller);

        var potion = await StackAsync(harness, ItemCatalog.DraughtOfMending, Rarity.Common, quantity: 2);

        var start = await harness.Combat.StartAsync(
            harness.UserId, MonsterCatalog.GiantRat, TestContext.Current.CancellationToken);

        var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);
        character.CurrentHitPoints = 3;
        character.HitPointsUpdatedAt = DateTimeOffset.UtcNow;
        await harness.Db.SaveChangesAsync();

        var round = await harness.Combat.UseItemAsync(
            harness.UserId, start.Value!.Id, potion.Id, TestContext.Current.CancellationToken);

        Assert.True(round.Ok);
        Assert.Equal(11, round.Value!.PlayerHitPoints);

        // A round happened: the number advanced and the monster took its half of it.
        Assert.Equal(1, round.Value.Encounter.Round);
        Assert.Contains(round.Value.Rolls, r => r.Actor == CombatRoll.Monster && r.Kind == "attack");

        // And the player did not swing.
        Assert.DoesNotContain(round.Value.Rolls, r => r.Actor == CombatRoll.Player && r.Kind == "attack");

        // Two lines: what was done, and what it changed. The second carries the flavour.
        var lines = round.Value.Rolls.Where(r => r.Actor == CombatRoll.Player).ToList();

        Assert.Equal(2, lines.Count);
        Assert.Contains("Draught of Mending", lines[0].Text, StringComparison.Ordinal);
        Assert.NotNull(lines[1].Flavour);

        Assert.Equal(1, script.RollCount);
        Assert.Equal([20], roller.Sides);

        // One unit gone, the rest of the stack still there.
        Assert.Equal(1, (await ReloadAsync(potion.Id))!.Quantity);
    }

    [Fact]
    public async Task The_last_unit_takes_the_row_with_it()
    {
        var harness = await ArrangeAsync(new SequenceDiceRoller(1));
        var potion = await StackAsync(harness, ItemCatalog.DraughtOfMending, Rarity.Common, quantity: 1);

        var start = await harness.Combat.StartAsync(
            harness.UserId, MonsterCatalog.GiantRat, TestContext.Current.CancellationToken);

        var round = await harness.Combat.UseItemAsync(
            harness.UserId, start.Value!.Id, potion.Id, TestContext.Current.CancellationToken);

        Assert.True(round.Ok);
        Assert.Null(await ReloadAsync(potion.Id));
    }

    /// <summary>
    /// A thrown pellet leaves the monster swinging at disadvantage, and that is the only die
    /// the whole business adds.
    /// </summary>
    [Fact]
    public async Task A_thrown_pellet_makes_the_monster_swing_at_disadvantage()
    {
        // The use round: no player dice, two d20s for the rattled rat. The round after: the
        // player's own d20 fumbles, and the rat is still rattled for its second swing.
        var script = new SequenceDiceRoller(1, 1, 1, 1, 1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller);

        var pellet = await StackAsync(harness, ItemCatalog.SmokePellet, Rarity.Common, quantity: 1);

        var start = await harness.Combat.StartAsync(
            harness.UserId, MonsterCatalog.GiantRat, TestContext.Current.CancellationToken);

        var thrown = await harness.Combat.UseItemAsync(
            harness.UserId, start.Value!.Id, pellet.Id, TestContext.Current.CancellationToken);

        var reply = thrown.Value!.Rolls.Single(r => r.Actor == CombatRoll.Monster && r.Kind == "attack");

        Assert.Equal(2, reply.Dice.Count);
        Assert.Single(reply.Dice, d => d.Kept);

        // Two applications, so it survives into the next round rather than being spent on the
        // swing it provoked the way the Bard's one-round remark is.
        var next = await harness.Combat.AttackAsync(
            harness.UserId, start.Value.Id, TestContext.Current.CancellationToken);

        var second = next.Value!.Rolls.Single(r => r.Actor == CombatRoll.Monster && r.Kind == "attack");

        Assert.Equal(2, second.Dice.Count);

        Assert.Equal(5, script.RollCount);
        Assert.Equal([20, 20, 20, 20, 20], roller.Sides);
    }

    /// <summary>A Rare potion is a better potion, with no new rule anywhere.</summary>
    [Fact]
    public async Task A_rarer_poison_bites_harder()
    {
        var harness = await ArrangeAsync(new SequenceDiceRoller(1));
        var vial = await StackAsync(harness, ItemCatalog.VialOfSerpentsKiss, Rarity.Rare, quantity: 1);

        var start = await harness.Combat.StartAsync(
            harness.UserId, MonsterCatalog.Goblin, TestContext.Current.CancellationToken);

        var round = await harness.Combat.UseItemAsync(
            harness.UserId, start.Value!.Id, vial.Id, TestContext.Current.CancellationToken);

        var poison = StatusEffects.Find(
            StatusEffects.Read(round.Value!.Encounter), EffectKind.Poisoned, EffectTarget.Monster);

        // Three at Common plus two for Rare, and the tick took the first application of it.
        Assert.NotNull(poison);
        Assert.Equal(5, poison.Magnitude);
        Assert.Equal(2, poison.Rounds);
        Assert.Equal(5, round.Value.Encounter.MonsterHitPoints);
    }

    [Fact]
    public async Task A_potion_cannot_be_worn()
    {
        var harness = await ArrangeAsync(new FixedDiceRoller(1));
        var potion = await StackAsync(harness, ItemCatalog.ElixirOfStone, Rarity.Common, quantity: 1);

        var result = await harness.Adventurer.EquipAsync(
            harness.UserId, potion.Id, TestContext.Current.CancellationToken);

        Assert.Equal(RpgFailure.ItemNotUsable, result.Failure);
        Assert.False((await ReloadAsync(potion.Id))!.IsEquipped);
    }

    [Fact]
    public async Task A_sword_cannot_be_drunk()
    {
        var harness = await ArrangeAsync(new SequenceDiceRoller(1));

        var sword = await harness.Db.InventoryItems
            .FirstAsync(i => i.UserId == harness.UserId && i.Slot == ItemSlot.Weapon);

        var start = await harness.Combat.StartAsync(
            harness.UserId, MonsterCatalog.GiantRat, TestContext.Current.CancellationToken);

        var round = await harness.Combat.UseItemAsync(
            harness.UserId, start.Value!.Id, sword.Id, TestContext.Current.CancellationToken);

        Assert.Equal(RpgFailure.ItemNotUsable, round.Failure);

        // The refusal cost nothing: no round, and no die that would shift a retry's sequence.
        var reloaded = await harness.Db.Encounters.FirstAsync(e => e.Id == start.Value.Id);

        Assert.Equal(0, reloaded.Round);
    }

    /// <summary>
    /// Selling and salvaging spend one unit, not the row.
    /// </summary>
    /// <remarks>
    /// Both used to remove the row outright, which was correct for every item in the game until
    /// one of them started meaning six. A stack of six potions sold for one potion's gold would
    /// have destroyed the other five without saying so.
    /// </remarks>
    [Fact]
    public async Task Selling_one_potion_leaves_the_rest_of_the_stack()
    {
        var harness = await ArrangeAsync(new FixedDiceRoller(1));
        var potion = await StackAsync(harness, ItemCatalog.WhetstoneOil, Rarity.Common, quantity: 4);

        var sold = await harness.Adventurer.SellAsync(
            harness.UserId, potion.Id, TestContext.Current.CancellationToken);

        Assert.True(sold.Ok);

        // One item's worth of gold, and three still in the bag.
        Assert.Equal(20, sold.Value!.GoldGained);
        Assert.Equal(3, (await ReloadAsync(potion.Id))!.Quantity);
    }

    [Fact]
    public async Task Salvaging_one_potion_leaves_the_rest_of_the_stack()
    {
        var harness = await ArrangeAsync(new FixedDiceRoller(1));
        var potion = await StackAsync(harness, ItemCatalog.ElixirOfStone, Rarity.Common, quantity: 4);

        var salvaged = await harness.Forge.SalvageAsync(
            harness.UserId, potion.Id, TestContext.Current.CancellationToken);

        Assert.True(salvaged.Ok);
        Assert.Equal(3, (await ReloadAsync(potion.Id))!.Quantity);
    }

    /// <summary>
    /// The upgrade bench refuses a stack.
    /// </summary>
    /// <remarks>
    /// Two reasons, either sufficient. A stack is one row, so raising its rarity would upgrade
    /// every potion on it for the price of one; and the new rarity may already have a row, in
    /// which case the write loses to the stacking index and comes back a 500 with the gold gone.
    /// </remarks>
    [Fact]
    public async Task The_bench_refuses_to_upgrade_a_stack()
    {
        var harness = await ArrangeAsync(new FixedDiceRoller(1));
        var potion = await StackAsync(harness, ItemCatalog.SmokePellet, Rarity.Common, quantity: 3);

        var upgraded = await harness.Shop.UpgradeAsync(
            harness.UserId, potion.Id, TestContext.Current.CancellationToken);

        Assert.Equal(RpgFailure.CannotUpgrade, upgraded.Failure);

        var reloaded = (await ReloadAsync(potion.Id))!;

        Assert.Equal(Rarity.Common, reloaded.Rarity);
        Assert.Equal(3, reloaded.Quantity);
    }
}
/// <summary>
/// The use route end to end, including the shape it puts on the wire.
/// </summary>
/// <remarks>
/// The DTO mirrors below are hand written and deliberately partial, in the manner of every
/// other endpoint test file here: they carry only the fields this file asserts on, and they are
/// what would fail to deserialise if one of those fields were renamed or retyped.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class ConsumableEndpointTests(PostgresFixture postgres) : IAsyncLifetime
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

    /// <summary>Buys a fight the only way anything can: by finishing real work.</summary>
    private static async Task GrantStaminaAsync(HttpClient client)
    {
        var created = await client.PostAsJsonAsync(
            "/api/tasks", new { title = "Real work", difficulty = "epic" });

        var task = await created.Content.ReadFromJsonAsync<IdDto>();

        var completed = await client.PostAsJsonAsync(
            $"/api/tasks/{task!.Id}/complete", new { utcOffsetMinutes = 0 });

        completed.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Puts a stack in the bag directly, because the shop is the only route to one and its
    /// shelf is a function of a user id the test cannot choose.
    /// </summary>
    private async Task<Guid> StockAsync(string subject, string itemKey, int quantity)
    {
        await using var db = postgres.CreateContext();

        var userId = await db.Users
            .Where(u => u.Auth0Sub == subject)
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

    private async Task<EncounterDto> StartAsync(HttpClient client)
    {
        var start = await client.PostAsJsonAsync(
            "/api/rpg/encounters", new { monsterKey = MonsterCatalog.GiantRat });

        start.EnsureSuccessStatusCode();

        return (await start.Content.ReadFromJsonAsync<EncounterDto>())!;
    }

    [Fact]
    public async Task The_use_route_requires_authentication()
    {
        using var anonymous = _factory.CreateAnonymousClient();

        var used = await anonymous.PostAsync(
            $"/api/rpg/encounters/{Guid.NewGuid()}/use/{Guid.NewGuid()}", null);

        Assert.Equal(HttpStatusCode.Unauthorized, used.StatusCode);
    }

    [Fact]
    public async Task Using_a_draught_is_a_round_and_comes_back_as_one()
    {
        await ChooseClassAsync(_alice);
        await GrantStaminaAsync(_alice);

        var potionId = await StockAsync("auth0|alice", ItemCatalog.DraughtOfMending, quantity: 2);
        var encounter = await StartAsync(_alice);

        // DEC-012 reaches the newest route too. Nothing in the RPG layer may pay experience,
        // and a new verb is exactly where that would be forgotten.
        var before = await _alice.GetFromJsonAsync<CharacterDto>("/api/character");

        var used = await _alice.PostAsync(
            $"/api/rpg/encounters/{encounter.Id}/use/{potionId}", null);

        used.EnsureSuccessStatusCode();

        var after = await _alice.GetFromJsonAsync<CharacterDto>("/api/character");

        Assert.Equal(before!.TotalXp, after!.TotalXp);
        Assert.Equal(before.Level, after.Level);

        var round = await used.Content.ReadFromJsonAsync<AttackDto>();

        // The same response shape an attack returns, because it is the same kind of thing.
        Assert.Equal(1, round!.Encounter.Round);
        Assert.NotEmpty(round.Rolls);

        // The player spent the round on the draught rather than on a swing.
        Assert.DoesNotContain(round.Rolls, r => r.Actor == "player" && r.Kind == "attack");
        Assert.Contains(round.Rolls, r => r.Actor == "monster" && r.Kind == "attack");

        // One unit gone, and the card says what the rest of them do.
        var bag = (await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory"))!;
        var stack = Assert.Single(bag, i => i.ItemKey == ItemCatalog.DraughtOfMending);

        Assert.Equal(1, stack.Quantity);
        Assert.Equal("consumable", stack.Slot);
        Assert.Contains("8 hit points", stack.UseDescription!, StringComparison.Ordinal);

        // Everything worn reports one and no use at all, so the client has one field to read.
        Assert.All(
            bag.Where(i => i.Slot != "consumable"),
            i =>
            {
                Assert.Equal(1, i.Quantity);
                Assert.Null(i.UseDescription);
            });
    }

    /// <summary>
    /// An effect applied in a round is on the wire in that round's response, and is still there
    /// on the next request that asks for the fight.
    /// </summary>
    /// <remarks>
    /// The whole status effect UI hangs off this one array. The server derived it, persisted it
    /// and read it back correctly while the mapper dropped it, so both strips rendered nothing
    /// in every fight ever played and nothing failed: the client field is optional, so the
    /// TypeScript build stayed green, and every hand-written mirror in this suite omitted it too.
    /// Asserted through the wire rather than off the entity for exactly that reason.
    /// </remarks>
    [Fact]
    public async Task An_effect_applied_in_a_round_comes_back_on_the_wire()
    {
        await ChooseClassAsync(_alice);
        await GrantStaminaAsync(_alice);

        var vialId = await StockAsync("auth0|alice", ItemCatalog.VialOfSerpentsKiss, quantity: 1);
        var encounter = await StartAsync(_alice);

        // A fight with nothing riding it carries the array all the same, empty.
        Assert.Empty(encounter.Effects);

        var used = await _alice.PostAsync(
            $"/api/rpg/encounters/{encounter.Id}/use/{vialId}", null);

        used.EnsureSuccessStatusCode();

        var round = (await used.Content.ReadFromJsonAsync<AttackDto>())!;
        var poison = Assert.Single(round.Encounter.Effects, e => e.Kind == "poisoned");

        // Lowercased at the mapping site the way status is, because the strip keys its icons
        // and its colours off these two strings.
        Assert.Equal("monster", poison.Target);
        Assert.Equal(3, poison.Magnitude);
        Assert.Equal(ItemCatalog.VialOfSerpentsKiss, poison.Source);

        // The tick took one application at the end of the round, and the strip has to be able
        // to count down. Two left, not three.
        Assert.Equal(2, poison.Rounds);

        // And a reload sees the same thing, because it is read off the row rather than off the
        // round that happened to apply it.
        var reloaded = await _alice.GetFromJsonAsync<EncounterDto>("/api/rpg/encounters/active");

        var stillPoisoned = Assert.Single(reloaded!.Effects, e => e.Kind == "poisoned");

        Assert.Equal("monster", stillPoisoned.Target);
        Assert.Equal(2, stillPoisoned.Rounds);
    }

    [Fact]
    public async Task Drinking_something_that_is_worn_is_a_bad_request()
    {
        await ChooseClassAsync(_alice);
        await GrantStaminaAsync(_alice);

        var bag = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");
        var sword = bag!.First(i => i.Slot == "weapon");

        var encounter = await StartAsync(_alice);

        var used = await _alice.PostAsync(
            $"/api/rpg/encounters/{encounter.Id}/use/{sword.Id}", null);

        // 400 rather than 409: no amount of waiting makes a sword drinkable.
        Assert.Equal(HttpStatusCode.BadRequest, used.StatusCode);
    }

    [Fact]
    public async Task Another_persons_potion_is_indistinguishable_from_one_that_never_existed()
    {
        await ChooseClassAsync(_alice);
        await GrantStaminaAsync(_alice);
        await ChooseClassAsync(_bob);

        var bobsPotion = await StockAsync("auth0|bob", ItemCatalog.SmokePellet, quantity: 3);
        var encounter = await StartAsync(_alice);

        var used = await _alice.PostAsync(
            $"/api/rpg/encounters/{encounter.Id}/use/{bobsPotion}", null);

        Assert.Equal(HttpStatusCode.NotFound, used.StatusCode);

        // And Bob still has all three.
        await using var db = postgres.CreateContext();

        Assert.Equal(
            3,
            (await db.InventoryItems.SingleAsync(
                i => i.Id == bobsPotion, TestContext.Current.CancellationToken)).Quantity);
    }

    [Fact]
    public async Task A_potion_cannot_be_equipped_over_the_wire()
    {
        await ChooseClassAsync(_alice);

        var potionId = await StockAsync("auth0|alice", ItemCatalog.WhetstoneOil, quantity: 1);

        var equipped = await _alice.PostAsync($"/api/rpg/inventory/{potionId}/equip", null);

        Assert.Equal(HttpStatusCode.BadRequest, equipped.StatusCode);
    }

    private sealed record IdDto(Guid Id);

    private sealed record CharacterDto(int Level, int TotalXp);

    private sealed record EncounterDto(Guid Id, string Status, int Round, int Phase, string? PhaseName, List<StatusEffectDto> Effects);

    private sealed record StatusEffectDto(string Kind, string Target, int Rounds, int Magnitude, string Source);

    private sealed record RollDto(string Actor, string Kind, string Text, string? Flavour);

    private sealed record AttackDto(EncounterDto Encounter, List<RollDto> Rolls);

    private sealed record ItemDto(
        Guid Id, string ItemKey, string Slot, int Quantity, string? UseDescription);
}
