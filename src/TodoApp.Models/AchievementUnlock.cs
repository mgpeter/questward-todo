namespace TodoApp.Models;

/// <summary>
/// Records that an achievement from <see cref="Progression.AchievementCatalog"/> has been
/// earned. The catalog itself lives in code, so adding badges never needs a migration.
/// </summary>
public class AchievementUnlock
{
    public int Id { get; set; }

    /// <summary>Owning user. Uniqueness is per user per badge, not per badge.</summary>
    public Guid UserId { get; set; }

    public string AchievementKey { get; set; } = string.Empty;

    public DateTimeOffset UnlockedAt { get; set; } = DateTimeOffset.UtcNow;
}
