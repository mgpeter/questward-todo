using TodoApp.Models.Dice;

namespace TodoApp.Tests.Infrastructure;

/// <summary>
/// A scripted roller. Combat is only testable because every roll goes through
/// <see cref="IDiceRoller"/>, and this is what that seam exists for.
/// </summary>
public sealed class SequenceDiceRoller(params int[] values) : IDiceRoller
{
    private readonly Queue<int> _values = new(values);

    public int RollCount { get; private set; }

    public int Roll(int sides)
    {
        RollCount++;

        if (_values.Count == 0)
        {
            throw new InvalidOperationException(
                $"The dice script ran out after {RollCount} rolls. A rule change probably " +
                "altered how many dice are rolled; extend the script rather than looping it.");
        }

        var value = _values.Dequeue();

        return Math.Clamp(value, 1, sides);
    }
}

/// <summary>Always rolls the same face. Useful when the value does not matter.</summary>
public sealed class FixedDiceRoller(int value) : IDiceRoller
{
    public int Roll(int sides) => Math.Clamp(value, 1, sides);
}

/// <summary>
/// Wraps another roller and records the size of every die asked for, in order.
/// </summary>
/// <remarks>
/// A roll count alone cannot tell a d20 that moved from a d100 that appeared, and the whole
/// point of the flavour work is that narration costs no die at all. Recording the shape of the
/// stream is what lets a test say "this fight consumed exactly these dice, in this order" and
/// fail the moment anything reaches for the roller that did not before.
/// </remarks>
public sealed class RecordingDiceRoller(IDiceRoller inner) : IDiceRoller
{
    private readonly List<int> _sides = [];

    /// <summary>Sides requested, in request order.</summary>
    public IReadOnlyList<int> Sides => _sides;

    public int Roll(int sides)
    {
        _sides.Add(sides);

        return inner.Roll(sides);
    }
}
