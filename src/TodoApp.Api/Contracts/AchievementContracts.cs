namespace TodoApp.Api.Contracts;

public sealed record AchievementDto(
    string Key,
    string Name,
    string Description,
    string Hint,
    string Icon,
    bool Unlocked,
    DateTimeOffset? UnlockedAt);
