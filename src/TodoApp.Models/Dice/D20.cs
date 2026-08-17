namespace TodoApp.Models.Dice;

/// <summary>
/// The d20 core: roll a twenty, add modifiers, compare to a target number.
/// </summary>
public static class D20
{
    public const int Sides = 20;

    /// <summary>
    /// An attack roll against an armour class.
    /// </summary>
    /// <param name="criticalOn">
    /// Lowest natural roll that counts as a critical. 20 normally; the Rogue's Sneak Attack
    /// lowers it to 19.
    /// </param>
    /// <remarks>
    /// A natural 20 always hits and a natural 1 always misses, regardless of the
    /// arithmetic. That override is the rule, not an edge case: it is what keeps a heavily
    /// armoured monster beatable and a weak one dangerous.
    /// </remarks>
    public static RollResult Attack(
        IDiceRoller roller,
        IReadOnlyList<RollModifier> modifiers,
        int armourClass,
        RollMode mode = RollMode.Normal,
        int criticalOn = 20)
    {
        var dice = RollWithMode(roller, mode);
        var natural = dice.Single(d => d.Kept).Value;
        var total = natural + modifiers.Sum(m => m.Value);

        var criticalHit = natural >= criticalOn;
        var criticalMiss = natural == 1;

        var outcome = criticalMiss
            ? RollOutcome.Miss
            : criticalHit || total >= armourClass
                ? RollOutcome.Hit
                : RollOutcome.Miss;

        return new RollResult(
            RollKind.Attack,
            dice,
            modifiers,
            total,
            armourClass,
            outcome,
            Critical: criticalHit && !criticalMiss,
            CriticalFailure: criticalMiss);
    }

    /// <summary>Damage for a landed hit. A critical doubles the dice but not the modifiers.</summary>
    public static RollResult Damage(
        IDiceRoller roller,
        DiceExpression expression,
        IReadOnlyList<RollModifier> modifiers,
        bool critical = false)
    {
        var dice = expression.RollDice(roller, doubleDice: critical);

        // Damage never drops below 1: a hit that heals the target reads as a bug.
        var total = Math.Max(
            1,
            dice.Sum(d => d.Value) + expression.Flat + modifiers.Sum(m => m.Value));

        return new RollResult(RollKind.Damage, dice, modifiers, total, Critical: critical);
    }

    private static List<DieRoll> RollWithMode(IDiceRoller roller, RollMode mode)
    {
        if (mode == RollMode.Normal)
        {
            return [new DieRoll(Sides, roller.Roll(Sides))];
        }

        var first = roller.Roll(Sides);
        var second = roller.Roll(Sides);

        // On a tie the first die is kept, so exactly one is ever marked as counting.
        var keepFirst = mode == RollMode.Advantage ? first >= second : first <= second;

        // Both dice stay in the breakdown, with the discarded one flagged, so the client
        // can show what advantage actually bought.
        return
        [
            new DieRoll(Sides, first, keepFirst),
            new DieRoll(Sides, second, !keepFirst)
        ];
    }
}
