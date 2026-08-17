namespace TodoApp.Models;

/// <summary>
/// The single local profile. Exactly one row exists, with <see cref="Id"/> pinned to
/// <see cref="SingletonId"/> by a check constraint.
/// </summary>
public class Character
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

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
