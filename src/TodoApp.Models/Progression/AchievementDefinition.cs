namespace TodoApp.Models.Progression;

/// <param name="Key">Stable identifier persisted in the database.</param>
/// <param name="Name">Display name on the badge.</param>
/// <param name="Description">Shown once unlocked.</param>
/// <param name="Hint">Shown while still locked, so the goal is discoverable.</param>
/// <param name="Icon">Emoji rendered on the badge face.</param>
public sealed record AchievementDefinition(
    string Key,
    string Name,
    string Description,
    string Hint,
    string Icon);
