using System.Security.Cryptography;
using System.Text;

namespace TodoApp.Models.Dice;

/// <summary>
/// A deterministic roller: the same seed always produces the same sequence.
/// </summary>
/// <remarks>
/// Used for the shop, whose daily stock is computed from the user and the date rather than
/// stored. That means no stock table, no nightly job, and a shop that is identical on
/// every request all day and different tomorrow. Deriving from
/// <see cref="IDiceRoller"/> lets it reuse the loot and rarity logic unchanged.
/// </remarks>
public sealed class SeededDiceRoller : IDiceRoller
{
    private uint _state;

    public SeededDiceRoller(string seed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);

        // A stable hash: string.GetHashCode is randomised per process and would give a
        // different shop on every restart.
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(seed));

        _state = BitConverter.ToUInt32(digest, 0);

        // xorshift stalls permanently on zero.
        if (_state == 0)
        {
            _state = 0x9E3779B9;
        }
    }

    public int Roll(int sides)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sides, 1);

        // xorshift32: small, fast, and entirely adequate for deciding what a shopkeeper
        // put on the shelf this morning.
        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;

        return (int)(_state % (uint)sides) + 1;
    }

    /// <summary>The seed for a user's stock on a given day.</summary>
    public static string DailySeed(Guid userId, DateOnly date) => $"shop:{userId:N}:{date:yyyy-MM-dd}";
}
