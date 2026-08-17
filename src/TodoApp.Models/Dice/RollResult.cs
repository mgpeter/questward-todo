namespace TodoApp.Models.Dice;

/// <param name="Kept">
/// False for a die discarded by advantage or disadvantage. Discarded dice are kept in the
/// breakdown on purpose, so the client can show what was rolled and thrown away.
/// </param>
public sealed record DieRoll(int Sides, int Value, bool Kept = true);

public sealed record RollModifier(string Label, int Value);

public enum RollOutcome
{
    None = 0,
    Hit = 1,
    Miss = 2
}

public enum RollMode
{
    Normal = 0,
    Advantage = 1,
    Disadvantage = 2
}

public enum RollKind
{
    Attack = 0,
    Damage = 1,
    Loot = 2
}

/// <summary>
/// A fully itemised roll: the dice, every modifier with its label, the total and what it
/// was measured against.
/// </summary>
/// <remarks>
/// The breakdown is the product, not debug output. Showing "d20: 14 +3 DEX +2 prof = 19 vs
/// AC 15" is what makes a miss read as bad luck rather than an arbitrary verdict.
/// </remarks>
public sealed record RollResult(
    RollKind Kind,
    IReadOnlyList<DieRoll> Dice,
    IReadOnlyList<RollModifier> Modifiers,
    int Total,
    int? Target = null,
    RollOutcome Outcome = RollOutcome.None,
    bool Critical = false,
    bool CriticalFailure = false)
{
    /// <summary>The die that actually counted, for a single-die roll such as a d20 check.</summary>
    public int NaturalRoll => Dice.FirstOrDefault(d => d.Kept)?.Value ?? 0;

    public string Describe()
    {
        var dice = string.Join(" ", Dice.Select(d => d.Kept ? $"d{d.Sides}:{d.Value}" : $"(d{d.Sides}:{d.Value})"));
        var mods = string.Concat(Modifiers.Select(m => m.Value >= 0 ? $" +{m.Value} {m.Label}" : $" {m.Value} {m.Label}"));
        var target = Target is null ? string.Empty : $" vs {Target}";

        return $"{dice}{mods} = {Total}{target}";
    }
}
