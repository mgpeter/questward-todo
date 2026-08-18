namespace TodoApp.Models.Rpg;

/// <summary>
/// What a reroll of today's shelf costs, and how many are left.
/// </summary>
/// <remarks>
/// A reroll hands back a whole fresh shelf, every offer buyable, which on its own would undo
/// the daily purchase cap that exists because the shop was once an uncapped gold-to-essence
/// pump: buy six, break them at the forge, reroll, buy six more.
/// <para>
/// The ladder is what closes that. The first reroll is cheap enough to be an ordinary thing to
/// do when you dislike the shelf; by the third it costs more stamina than a day's work, and the
/// seventh costs a thousand. Anyone grinding it is paying far more in real tasks than the
/// essence is worth, and after seven the answer is simply no.
/// </para>
/// </remarks>
public static class ShopRerolls
{
    /// <summary>
    /// Stamina for the first reroll, the second, and so on. Chosen by the product owner.
    /// </summary>
    public static readonly IReadOnlyList<int> Ladder = [1, 10, 50, 100, 200, 500, 1000];

    /// <summary>The most rerolls a shelf will take in one day.</summary>
    public static int MaxPerDay => Ladder.Count;

    /// <summary>
    /// What the next reroll costs, or null when the day's ladder is spent.
    /// </summary>
    /// <param name="alreadyRerolled">How many rerolls have already been paid for today.</param>
    public static int? CostOf(int alreadyRerolled) =>
        alreadyRerolled >= 0 && alreadyRerolled < Ladder.Count ? Ladder[alreadyRerolled] : null;

    /// <summary>Total stamina to walk the whole ladder, which is 1861.</summary>
    public static int WholeLadder => Ladder.Sum();
}

/// <summary>
/// One paid reroll of one user's shelf on one day.
/// </summary>
/// <remarks>
/// Counting rows rather than keeping a counter column, so the generation and the number of
/// rerolls paid for are the same fact and cannot disagree. The unique index on
/// (UserId, Day, Generation) is what stops two concurrent requests both buying generation 1 and
/// leaving one of them having paid for a shelf it never got.
/// </remarks>
public class ShopReroll
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    /// <summary>The UTC day whose shelf was rerolled. Stored as a date, matching the seed.</summary>
    public DateOnly Day { get; set; }

    /// <summary>Which shelf this reroll bought: 1 for the first, 2 for the second.</summary>
    public int Generation { get; set; }

    /// <summary>Stamina actually paid, snapshotted so retuning the ladder cannot rewrite history.</summary>
    public int StaminaPaid { get; set; }

    public DateTimeOffset RerolledAt { get; set; } = DateTimeOffset.UtcNow;
}
