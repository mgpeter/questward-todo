using System.Globalization;
using System.Text.RegularExpressions;

namespace TodoApp.Models.Dice;

/// <summary>
/// A dice notation expression such as <c>1d20</c>, <c>2d6+3</c> or <c>1d8-1</c>.
/// </summary>
public readonly partial record struct DiceExpression(int Count, int Sides, int Flat)
{
    [GeneratedRegex(@"^\s*(\d*)d(\d+)\s*(?:([+-])\s*(\d+))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Notation();

    public static DiceExpression Parse(string notation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notation);

        var match = Notation().Match(notation);

        if (!match.Success)
        {
            throw new FormatException($"'{notation}' is not dice notation (expected forms like 2d6+3).");
        }

        var count = match.Groups[1].Value is "" ? 1 : int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var sides = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

        var flat = 0;

        if (match.Groups[4].Success)
        {
            flat = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
            if (match.Groups[3].Value == "-") flat = -flat;
        }

        if (count < 1) throw new FormatException($"'{notation}' must roll at least one die.");
        if (sides < 2) throw new FormatException($"'{notation}' must use a die with at least two sides.");

        return new DiceExpression(count, sides, flat);
    }

    public static bool TryParse(string? notation, out DiceExpression expression)
    {
        try
        {
            expression = Parse(notation!);
            return true;
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            expression = default;
            return false;
        }
    }

    /// <param name="doubleDice">
    /// Doubles the number of dice but not the flat bonus, which is how a critical hit
    /// works at the table.
    /// </param>
    public IReadOnlyList<DieRoll> RollDice(IDiceRoller roller, bool doubleDice = false)
    {
        var count = doubleDice ? Count * 2 : Count;
        var dice = new List<DieRoll>(count);

        for (var i = 0; i < count; i++)
        {
            dice.Add(new DieRoll(Sides, roller.Roll(Sides)));
        }

        return dice;
    }

    /// <summary>Highest possible total, used for max hit points at level 1.</summary>
    public int Max => (Count * Sides) + Flat;

    /// <summary>
    /// The tabletop "take the average" rule for levelling: half the die plus one.
    /// </summary>
    public int Average => (Count * ((Sides / 2) + 1)) + Flat;

    public override string ToString() =>
        Flat switch
        {
            0 => $"{Count}d{Sides}",
            > 0 => $"{Count}d{Sides}+{Flat}",
            _ => $"{Count}d{Sides}{Flat}"
        };
}
