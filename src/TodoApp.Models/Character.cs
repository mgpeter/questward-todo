namespace TodoApp.Models;

/// <summary>
/// A user's game state. Exactly one per <see cref="User"/>, enforced by using
/// <see cref="UserId"/> as the primary key rather than a surrogate.
/// </summary>
public class Character
{
    public Guid UserId { get; set; }

    public string Name { get; set; } = "Adventurer";

    /// <summary>Key into the avatar set defined by the client.</summary>
    public string AvatarKey { get; set; } = "fox";

    /// <summary>
    /// Source of truth for progression. Level is never stored, it is always derived from
    /// this via <see cref="Progression.LevelCurve"/> so the two can never disagree.
    /// </summary>
    public int TotalXp { get; set; }

    public int TasksCompleted { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
