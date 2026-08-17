using System.Security.Cryptography;

namespace TodoApp.Models.Dice;

/// <summary>
/// The only source of randomness in the domain.
/// </summary>
/// <remarks>
/// Nothing in the combat rules may call <c>Random</c> directly. Every roll goes through
/// this seam, which is what makes the rules exhaustively testable: tests inject a scripted
/// sequence and assert on outcomes that would otherwise be unreproducible.
/// </remarks>
public interface IDiceRoller
{
    /// <summary>Rolls a single die, returning a value in [1, sides].</summary>
    int Roll(int sides);
}

public sealed class SecureDiceRoller : IDiceRoller
{
    public int Roll(int sides)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sides, 1);

        return RandomNumberGenerator.GetInt32(1, sides + 1);
    }
}
