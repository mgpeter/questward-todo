using TodoApp.Models.Dice;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

public class DiceExpressionTests
{
    [Theory]
    [InlineData("1d20", 1, 20, 0)]
    [InlineData("d20", 1, 20, 0)]
    [InlineData("2d6+3", 2, 6, 3)]
    [InlineData("1d8-1", 1, 8, -1)]
    [InlineData(" 3d10 + 2 ", 3, 10, 2)]
    [InlineData("1D12", 1, 12, 0)]
    public void Parses_dice_notation(string notation, int count, int sides, int flat)
    {
        var expression = DiceExpression.Parse(notation);

        Assert.Equal(count, expression.Count);
        Assert.Equal(sides, expression.Sides);
        Assert.Equal(flat, expression.Flat);
    }

    [Theory]
    [InlineData("")]
    [InlineData("20")]
    [InlineData("d")]
    [InlineData("0d6")]
    [InlineData("1d1")]
    [InlineData("two d six")]
    public void Rejects_anything_that_is_not_dice_notation(string notation) =>
        Assert.False(DiceExpression.TryParse(notation, out _));

    [Theory]
    [InlineData("1d8", "1d8")]
    [InlineData("2d6+3", "2d6+3")]
    [InlineData("1d8-1", "1d8-1")]
    public void Round_trips_through_its_own_string_form(string notation, string expected) =>
        Assert.Equal(expected, DiceExpression.Parse(notation).ToString());

    [Fact]
    public void Reports_max_and_average_for_hit_point_calculations()
    {
        var d10 = DiceExpression.Parse("1d10");

        Assert.Equal(10, d10.Max);
        Assert.Equal(6, d10.Average); // half the die plus one, the tabletop levelling rule
    }

    [Fact]
    public void Rolls_one_die_per_count()
    {
        var roller = new SequenceDiceRoller(4, 5, 6);
        var dice = DiceExpression.Parse("3d6").RollDice(roller);

        Assert.Equal([4, 5, 6], dice.Select(d => d.Value));
        Assert.All(dice, d => Assert.Equal(6, d.Sides));
    }

    [Fact]
    public void Doubles_the_dice_but_not_the_flat_bonus_on_a_critical()
    {
        var roller = new SequenceDiceRoller(3, 3, 3, 3);
        var dice = DiceExpression.Parse("2d6+5").RollDice(roller, doubleDice: true);

        Assert.Equal(4, dice.Count);
    }
}

public class D20AttackTests
{
    private static readonly IReadOnlyList<RollModifier> PlusFive =
        [new RollModifier("STR", 3), new RollModifier("proficiency", 2)];

    [Fact]
    public void Hits_when_the_total_reaches_the_armour_class()
    {
        var result = D20.Attack(new SequenceDiceRoller(10), PlusFive, armourClass: 15);

        Assert.Equal(15, result.Total);
        Assert.Equal(RollOutcome.Hit, result.Outcome);
        Assert.False(result.Critical);
    }

    [Fact]
    public void Misses_when_the_total_falls_short()
    {
        var result = D20.Attack(new SequenceDiceRoller(9), PlusFive, armourClass: 15);

        Assert.Equal(14, result.Total);
        Assert.Equal(RollOutcome.Miss, result.Outcome);
    }

    [Fact]
    public void A_natural_twenty_hits_however_high_the_armour_class()
    {
        // The whole point of the rule: nothing is unbeatable.
        var result = D20.Attack(new SequenceDiceRoller(20), [], armourClass: 99);

        Assert.Equal(RollOutcome.Hit, result.Outcome);
        Assert.True(result.Critical);
    }

    [Fact]
    public void A_natural_one_misses_however_large_the_bonus()
    {
        // And the mirror of it: nothing is a foregone conclusion.
        var result = D20.Attack(
            new SequenceDiceRoller(1),
            [new RollModifier("absurd", 100)],
            armourClass: 5);

        Assert.Equal(RollOutcome.Miss, result.Outcome);
        Assert.True(result.CriticalFailure);
        Assert.False(result.Critical);
    }

    [Fact]
    public void A_rogue_crits_on_nineteen_as_well()
    {
        var normal = D20.Attack(new SequenceDiceRoller(19), [], armourClass: 10);
        var rogue = D20.Attack(new SequenceDiceRoller(19), [], armourClass: 10, criticalOn: 19);

        Assert.False(normal.Critical);
        Assert.True(rogue.Critical);
    }

    [Fact]
    public void Advantage_keeps_the_higher_die_and_records_the_discarded_one()
    {
        var result = D20.Attack(new SequenceDiceRoller(4, 17), [], armourClass: 10, mode: RollMode.Advantage);

        Assert.Equal(17, result.NaturalRoll);
        Assert.Equal(2, result.Dice.Count);
        Assert.Contains(result.Dice, d => d is { Value: 4, Kept: false });
    }

    [Fact]
    public void Disadvantage_keeps_the_lower_die()
    {
        var result = D20.Attack(new SequenceDiceRoller(4, 17), [], armourClass: 10, mode: RollMode.Disadvantage);

        Assert.Equal(4, result.NaturalRoll);
        Assert.Contains(result.Dice, d => d is { Value: 17, Kept: false });
    }

    [Fact]
    public void Advantage_on_two_equal_dice_keeps_exactly_one()
    {
        var result = D20.Attack(new SequenceDiceRoller(11, 11), [], armourClass: 10, mode: RollMode.Advantage);

        Assert.Single(result.Dice, d => d.Kept);
    }

    [Fact]
    public void The_breakdown_carries_every_modifier_with_its_label()
    {
        var result = D20.Attack(new SequenceDiceRoller(12), PlusFive, armourClass: 15);

        Assert.Equal(["STR", "proficiency"], result.Modifiers.Select(m => m.Label));
        Assert.Contains("vs 15", result.Describe());
    }
}

public class D20DamageTests
{
    [Fact]
    public void Adds_the_flat_bonus_and_the_modifiers()
    {
        var result = D20.Damage(
            new SequenceDiceRoller(5),
            DiceExpression.Parse("1d8+1"),
            [new RollModifier("STR", 3)]);

        Assert.Equal(9, result.Total); // 5 + 1 flat + 3 STR
    }

    [Fact]
    public void A_critical_rolls_twice_the_dice()
    {
        var result = D20.Damage(
            new SequenceDiceRoller(4, 6),
            DiceExpression.Parse("1d8"),
            [],
            critical: true);

        Assert.Equal(2, result.Dice.Count);
        Assert.Equal(10, result.Total);
        Assert.True(result.Critical);
    }

    [Fact]
    public void Never_falls_below_one()
    {
        // A hit that heals the target reads as a bug, whatever the arithmetic says.
        var result = D20.Damage(
            new SequenceDiceRoller(1),
            DiceExpression.Parse("1d4"),
            [new RollModifier("withering", -10)]);

        Assert.Equal(1, result.Total);
    }
}
