using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Services.Rpg;
using TodoApp.Models.Dice;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

public class SeededDiceRollerTests
{
    [Fact]
    public void The_same_seed_always_produces_the_same_sequence()
    {
        var a = new SeededDiceRoller("shop:abc:2026-08-17");
        var b = new SeededDiceRoller("shop:abc:2026-08-17");

        var first = Enumerable.Range(0, 40).Select(_ => a.Roll(100)).ToList();
        var second = Enumerable.Range(0, 40).Select(_ => b.Roll(100)).ToList();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Different_seeds_diverge()
    {
        var monday = new SeededDiceRoller("shop:abc:2026-08-17");
        var tuesday = new SeededDiceRoller("shop:abc:2026-08-18");

        var a = Enumerable.Range(0, 40).Select(_ => monday.Roll(100)).ToList();
        var b = Enumerable.Range(0, 40).Select(_ => tuesday.Roll(100)).ToList();

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Stays_within_the_die()
    {
        var roller = new SeededDiceRoller("bounds");

        foreach (var sides in new[] { 2, 4, 6, 20, 100 })
        {
            for (var i = 0; i < 200; i++)
            {
                Assert.InRange(roller.Roll(sides), 1, sides);
            }
        }
    }

    [Fact]
    public void Survives_a_process_restart()
    {
        // The seed is hashed rather than run through string.GetHashCode, which is
        // randomised per process and would reshuffle the shop on every restart.
        Assert.Equal(
            new SeededDiceRoller("stable").Roll(1000),
            new SeededDiceRoller("stable").Roll(1000));
    }
}

public class ShopStockTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Monday = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Stock_is_identical_all_day()
    {
        var morning = ShopService.StockFor(Alice, Monday);
        var evening = ShopService.StockFor(Alice, Monday.AddHours(14));

        Assert.Equal(
            morning.Offers.Select(o => o.OfferId),
            evening.Offers.Select(o => o.OfferId));
    }

    [Fact]
    public void Stock_rotates_the_next_day()
    {
        var monday = ShopService.StockFor(Alice, Monday);
        var tuesday = ShopService.StockFor(Alice, Monday.AddDays(1));

        Assert.NotEqual(
            monday.Offers.Select(o => o.OfferId),
            tuesday.Offers.Select(o => o.OfferId));
    }

    [Fact]
    public void Two_shoppers_see_different_shelves()
    {
        Assert.NotEqual(
            ShopService.StockFor(Alice, Monday).Offers.Select(o => o.OfferId),
            ShopService.StockFor(Bob, Monday).Offers.Select(o => o.OfferId));
    }

    [Fact]
    public void No_item_appears_twice_on_one_shelf()
    {
        var stock = ShopService.StockFor(Alice, Monday);

        Assert.Equal(
            stock.Offers.Count,
            stock.Offers.Select(o => o.Item.Key).Distinct().Count());
    }

    [Fact]
    public void The_shop_never_stocks_epic_or_legendary()
    {
        // Gold is plentiful once fights are going. A shop selling the best gear would make
        // loot drops pointless, so the top tiers have to be won or upgraded into.
        for (var day = 0; day < 120; day++)
        {
            var stock = ShopService.StockFor(Alice, Monday.AddDays(day));

            Assert.All(stock.Offers, o =>
                Assert.True(o.Rarity <= ShopService.MaxStockRarity, $"day {day}: {o.Rarity}"));
        }
    }

    [Fact]
    public void Every_offer_is_priced_and_real()
    {
        var stock = ShopService.StockFor(Alice, Monday);

        Assert.Equal(ShopService.OfferCount, stock.Offers.Count);
        Assert.All(stock.Offers, o =>
        {
            Assert.True(ItemCatalog.Exists(o.Item.Key));
            Assert.True(o.Price > 0);
            Assert.Equal(o.Item.ValueAt(o.Rarity), o.Price);
        });
    }

    [Fact]
    public void Buying_costs_more_than_selling_returns()
    {
        // The spread is the gold sink. If they matched, gold would be meaningless.
        var stock = ShopService.StockFor(Alice, Monday);

        Assert.All(stock.Offers, o =>
        {
            var sellValue = Math.Max(1, o.Item.ValueAt(o.Rarity) / 2);
            Assert.True(o.Price > sellValue);
        });
    }

    [Fact]
    public void Upgrade_cost_rises_with_the_target_tier()
    {
        var sword = ItemCatalog.Find(ItemCatalog.RustyLongsword)!;

        var toUncommon = ShopService.UpgradeCost(sword, Rarity.Uncommon);
        var toLegendary = ShopService.UpgradeCost(sword, Rarity.Legendary);

        Assert.True(toLegendary > toUncommon);
        Assert.True(toUncommon >= 25);
    }
}

public class RestCostTests
{
    [Fact]
    public void Costs_nothing_when_already_whole() =>
        Assert.Equal(0, AdventurerService.RestCost(0, 5));

    [Theory]
    [InlineData(1, 1, 5)]     // floor
    [InlineData(10, 1, 30)]
    [InlineData(10, 5, 70)]
    [InlineData(30, 3, 150)]
    public void Scales_with_missing_health_and_level(int missing, int level, int expected) =>
        Assert.Equal(expected, AdventurerService.RestCost(missing, level));

    [Fact]
    public void Is_always_worth_at_least_something() =>
        Assert.True(AdventurerService.RestCost(1, 1) >= 5);
}

[Collection(nameof(PostgresCollection))]
public class ClassAbilityTests(PostgresFixture postgres)
{
    private sealed record Harness(
        TodoApp.Data.TodoDbContext Db, CombatService Combat, AdventurerService Adventurer, Guid UserId);

    private async Task<Harness> ArrangeAsync(IDiceRoller roller, string classKey)
    {
        await postgres.ResetAsync();
        var user = await postgres.CreateUserAsync("test|hero");

        var db = postgres.CreateContext();
        var sheets = new CharacterSheetService(db);
        var loot = new LootService(db, roller);
        var quests = new QuestService(db, loot);
        var adventurer = new AdventurerService(db, sheets, loot);
        var combat = new CombatService(db, roller, sheets, loot, quests);

        await adventurer.ChooseClassAsync(user.Id, classKey, default);

        var character = await db.Characters.SingleAsync(c => c.UserId == user.Id);
        character.Stamina = 20;
        await db.SaveChangesAsync();

        return new Harness(db, combat, adventurer, user.Id);
    }

    private static SequenceDiceRoller Hits() => new(Enumerable.Repeat(15, 400).ToArray());

    [Fact]
    public async Task Every_class_has_at_least_one_ability()
    {
        foreach (var characterClass in ClassCatalog.All)
        {
            Assert.NotEmpty(ClassAbilities.For(characterClass.Key));
        }

        // A character with no class has none, and that must not throw.
        Assert.Empty(ClassAbilities.For(null));
    }

    [Fact]
    public async Task Magic_missile_skips_the_attack_roll_entirely()
    {
        // Scripted to always roll a 1: a normal attack would fumble every time, so any
        // damage at all proves the attack roll was bypassed.
        var harness = await ArrangeAsync(new SequenceDiceRoller(Enumerable.Repeat(1, 100).ToArray()), ClassCatalog.Wizard);

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);
        var before = start.Value!.MonsterHitPoints;

        var result = await harness.Combat.UseAbilityAsync(
            harness.UserId, start.Value.Id, ClassAbilities.MagicMissile, default);

        Assert.True(result.Ok);
        Assert.True(result.Value!.Encounter.MonsterHitPoints < before);

        // Scoped to the player: the monster still takes its own attack roll in reply.
        Assert.DoesNotContain(
            result.Value.Rolls,
            r => r.Actor == CombatRoll.Player && r.Kind == "attack");
    }

    [Fact]
    public async Task Healing_word_heals_and_forfeits_the_attack()
    {
        var harness = await ArrangeAsync(Hits(), ClassCatalog.Cleric);

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);

        var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);
        character.CurrentHitPoints = 3;
        character.HitPointsUpdatedAt = DateTimeOffset.UtcNow;
        await harness.Db.SaveChangesAsync();

        var monsterBefore = start.Value!.MonsterHitPoints;

        var result = await harness.Combat.UseAbilityAsync(
            harness.UserId, start.Value.Id, ClassAbilities.HealingWord, default);

        Assert.True(result.Ok);
        Assert.True(result.Value!.PlayerHitPoints > 3);

        // The monster took nothing: healing forfeits the swing.
        Assert.Equal(monsterBefore, result.Value.Encounter.MonsterHitPoints);
    }

    /// <summary>
    /// The Cleric's Blessing, which had no test at all until the attack path was rebuilt
    /// around status effects.
    /// </summary>
    /// <remarks>
    /// Two things are pinned here and they are separate. The first is the rule: one reroll per
    /// fight, on the first natural 1 and no other. The second is the cost in dice, because the
    /// reroll re-enters D20.Attack with the same mode and is therefore the one place in a round
    /// where a perk multiplies an effect. A Weakened Cleric who fumbles spends four d20s on a
    /// single swing, and nothing but a scripted count would notice if that quietly became two.
    /// </remarks>
    [Fact]
    public async Task The_first_natural_one_of_a_fight_is_rerolled_and_only_the_first()
    {
        // Round one: a natural 1, rerolled into a 2 that still misses a giant rat, then the
        // rat's own natural 1. Round two: a second natural 1, which stands, then another.
        var script = new SequenceDiceRoller(1, 2, 1, 1, 1);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller, ClassCatalog.Cleric);

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);
        Assert.True(start.Ok);

        var first = await harness.Combat.AttackAsync(harness.UserId, start.Value!.Id, default);
        var firstSwing = first.Value!.Rolls.First(
            r => r.Actor == CombatRoll.Player && r.Kind == "attack");

        // The 1 is gone and the 2 is what the log carries, so the reroll's result is the one
        // that counted rather than the fumble being merely forgiven.
        Assert.Equal(2, Assert.Single(firstSwing.Dice).Value);
        Assert.Equal("miss", firstSwing.Outcome);
        Assert.True(first.Value.Encounter.BlessingUsed);
        Assert.Equal(3, script.RollCount);

        var second = await harness.Combat.AttackAsync(harness.UserId, start.Value.Id, default);
        var secondSwing = second.Value!.Rolls.First(
            r => r.Actor == CombatRoll.Player && r.Kind == "attack");

        // Once per fight: the second natural 1 stands as a fumble.
        Assert.Equal(1, Assert.Single(secondSwing.Dice).Value);
        Assert.Equal("fumble", secondSwing.Outcome);

        // Five d20s and nothing else: two swings, two answers, and the one reroll between them.
        Assert.Equal(5, script.RollCount);
        Assert.Equal([20, 20, 20, 20, 20], roller.Sides);
    }

    [Fact]
    public async Task Power_attack_doubles_the_damage_dice()
    {
        var harness = await ArrangeAsync(Hits(), ClassCatalog.Fighter);

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Skeleton, default);

        var result = await harness.Combat.UseAbilityAsync(
            harness.UserId, start.Value!.Id, ClassAbilities.PowerAttack, default);

        var damage = result.Value!.Rolls.First(r => r.Kind == "damage");

        // A Fighter's longsword is 1d8; doubled is two dice.
        Assert.Equal(2, damage.Dice.Count);

        var attack = result.Value.Rolls.First(r => r.Kind == "attack");
        Assert.Contains(attack.Modifiers, m => m.Label == "power attack" && m.Value == -2);
    }

    [Fact]
    public async Task Sneak_strike_rolls_the_attack_with_advantage()
    {
        var harness = await ArrangeAsync(Hits(), ClassCatalog.Rogue);

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);

        var result = await harness.Combat.UseAbilityAsync(
            harness.UserId, start.Value!.Id, ClassAbilities.SneakStrike, default);

        var attack = result.Value!.Rolls.First(r => r.Kind == "attack");

        // Two dice with one discarded is what advantage looks like on the wire.
        Assert.Equal(2, attack.Dice.Count);
        Assert.Single(attack.Dice, d => d.Kept);
    }

    [Fact]
    public async Task Vicious_mockery_makes_the_answering_swing_go_wide()
    {
        var harness = await ArrangeAsync(Hits(), ClassCatalog.Bard);

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Skeleton, default);

        var mock = await harness.Combat.UseAbilityAsync(
            harness.UserId, start.Value!.Id, ClassAbilities.ViciousMockery, default);

        var monsterAttack = mock.Value!.Rolls.Single(
            r => r.Actor == CombatRoll.Monster && r.Kind == "attack");

        // Two dice with one discarded is what disadvantage looks like on the wire.
        Assert.Equal(2, monsterAttack.Dice.Count);
        Assert.Single(monsterAttack.Dice, d => d.Kept);

        // Consumed by that counter, so it does not linger into later rounds. Stated through the
        // effect array rather than the MonsterDisadvantageRounds column it replaced: same fact,
        // same round, new vocabulary.
        Assert.Null(StatusEffects.Find(
            StatusEffects.Read(mock.Value.Encounter), EffectKind.Weakened, EffectTarget.Monster));

        var next = await harness.Combat.AttackAsync(harness.UserId, start.Value.Id, default);
        var later = next.Value!.Rolls.FirstOrDefault(
            r => r.Actor == CombatRoll.Monster && r.Kind == "attack");

        if (later is not null)
        {
            Assert.Single(later.Dice);
        }
    }

    [Fact]
    public async Task An_ability_runs_out_after_its_uses()
    {
        var harness = await ArrangeAsync(Hits(), ClassCatalog.Ranger);

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Wraith, default);

        if (!start.Ok)
        {
            // Wraith is out of range at level 1; use something reachable instead.
            start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Skeleton, default);
        }

        var ability = ClassAbilities.For(ClassCatalog.Ranger)[0];

        for (var use = 0; use < ability.UsesPerEncounter; use++)
        {
            var ok = await harness.Combat.UseAbilityAsync(
                harness.UserId, start.Value!.Id, ability.Key, default);

            if (!ok.Ok || ok.Value!.Encounter.IsOver) return; // fight ended early, nothing to assert
        }

        var exhausted = await harness.Combat.UseAbilityAsync(
            harness.UserId, start.Value!.Id, ability.Key, default);

        Assert.False(exhausted.Ok);
        Assert.Equal(RpgFailure.AbilityExhausted, exhausted.Failure);
    }

    [Fact]
    public async Task A_class_cannot_use_another_classes_ability()
    {
        var harness = await ArrangeAsync(Hits(), ClassCatalog.Fighter);

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);

        var result = await harness.Combat.UseAbilityAsync(
            harness.UserId, start.Value!.Id, ClassAbilities.MagicMissile, default);

        Assert.False(result.Ok);
        Assert.Equal(RpgFailure.NotFound, result.Failure);
    }

    [Fact]
    public async Task Using_abilities_never_moves_experience()
    {
        // The invariant, re-asserted against the newest way to spend a round.
        var harness = await ArrangeAsync(Hits(), ClassCatalog.Wizard);

        var before = await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId);

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.Goblin, default);

        await harness.Combat.UseAbilityAsync(
            harness.UserId, start.Value!.Id, ClassAbilities.MagicMissile, default);

        var after = await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId);

        Assert.Equal(before.TotalXp, after.TotalXp);
        Assert.Equal(before.TasksCompleted, after.TasksCompleted);
    }
}
