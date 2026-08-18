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

/// <summary>
/// Rolls normally until it is armed, then throws on the next die asked for.
/// </summary>
/// <remarks>
/// The seam the transaction boundary test needs. A hunt settles by winning a fight, and the
/// first thing a win does is roll for gold, so a roller that throws on demand is the one way to
/// make settlement fail for a production reason at a production moment: after the completion has
/// already committed. Arming it rather than throwing from the first roll is what keeps the
/// arrangement, which rolls for starting gear and for the fight being interrupted, honest.
/// </remarks>
public sealed class FailingDiceRoller(IDiceRoller inner) : IDiceRoller
{
    /// <summary>The exception every armed roll throws, so a test can assert it was this one.</summary>
    public sealed class TrayKnockedOverException() : InvalidOperationException("The dice tray went over.");

    public bool Armed { get; set; }

    public int Roll(int sides) => Armed ? throw new TrayKnockedOverException() : inner.Roll(sides);
}
