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
