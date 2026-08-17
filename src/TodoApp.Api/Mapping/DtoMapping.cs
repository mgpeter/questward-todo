using TodoApp.Api.Contracts;
using TodoApp.Models;
using TodoApp.Models.Progression;

namespace TodoApp.Api.Mapping;

public static class DtoMapping
{
    public static TaskDto ToDto(this TodoTask task, IReadOnlyList<TodoTask>? subtasks = null)
    {
        // One reading of the clock for the whole projection, so a task cannot report a
        // status from before midnight and an overdue count from after it.
        var now = DateTimeOffset.UtcNow;

        return new TaskDto(
            task.Id,
            task.ParentId,
            task.Title,
            task.Notes,
            task.Difficulty,
            task.Priority,
            task.Tags,
            task.Difficulty.BaseXp(),
            task.DueDate,
            // Effective, not stored: a daily task ticked yesterday is open again today.
            task.StatusAt(now),
            task.IsCompletedAt(now),
            task.CompletedAt,
            task.StartedAt,
            task.XpAwarded,
            task.StaminaAwarded,
            task.Recurrence,
            // Surfaced so the UI can say "this pays nothing" rather than quietly awarding zero.
            task.MayAwardAt(now),
            task.DaysOverdue(now),
            task.SortOrder,
            subtasks?.Select(s => s.ToDto()).ToList() ?? [],
            task.CreatedAt,
            task.UpdatedAt);
    }

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
