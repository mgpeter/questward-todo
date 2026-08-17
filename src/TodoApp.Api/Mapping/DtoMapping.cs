using TodoApp.Api.Contracts;
using TodoApp.Models;
using TodoApp.Models.Progression;

namespace TodoApp.Api.Mapping;

public static class DtoMapping
{
    public static TaskDto ToDto(this TodoTask task) => new(
        task.Id,
        task.Title,
        task.Notes,
        task.Difficulty,
        task.Priority,
        task.Difficulty.BaseXp(),
        task.DueDate,
        task.IsCompleted,
        task.CompletedAt,
        task.XpAwarded,
        task.SortOrder,
        task.CreatedAt,
        task.UpdatedAt);

    public static CharacterDto ToDto(this Character character, int achievementsUnlocked)
    {
        var progress = LevelCurve.Describe(character.TotalXp);

        return new CharacterDto(
            character.Name,
            character.AvatarKey,
            progress.Level,
            progress.Title,
            progress.TotalXp,
            progress.XpIntoLevel,
            progress.XpForNextLevel,
            progress.XpToNextLevel,
            character.TasksCompleted,
            achievementsUnlocked,
            AchievementCatalog.All.Count,
            character.CreatedAt);
    }

    public static AchievementDto ToDto(this AchievementDefinition definition, DateTimeOffset? unlockedAt) =>
        new(
            definition.Key,
            definition.Name,
            definition.Description,
            definition.Hint,
            definition.Icon,
            unlockedAt.HasValue,
            unlockedAt);
}
