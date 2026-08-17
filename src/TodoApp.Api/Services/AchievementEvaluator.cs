using TodoApp.Models;
using TodoApp.Models.Progression;

namespace TodoApp.Api.Services;

/// <param name="OpenTasksBefore">Open tasks immediately before this completion, used by Clean Slate.</param>
/// <param name="LocalCompletedAt">Completion time in the client's local offset, used by the time-of-day badges.</param>
public sealed record AchievementContext(
    TodoTask CompletedTask,
    int TasksCompletedTotal,
    int Level,
    int HardOrEpicCompleted,
    int OpenTasksBefore,
    int OpenTasksAfter,
    int CompletedTodayLocal,
    DateTimeOffset LocalCompletedAt);

/// <summary>
/// Pure rules engine: given the state right after a completion, which badges are earned?
/// Deduplication against already-unlocked badges happens in <see cref="GamificationService"/>.
/// </summary>
public sealed class AchievementEvaluator
{
    public IReadOnlyList<string> Evaluate(AchievementContext context)
    {
        var earned = new List<string>();

        if (context.TasksCompletedTotal >= 1)
        {
            earned.Add(AchievementCatalog.FirstBlood);
        }

        if (context.TasksCompletedTotal >= 10)
        {
            earned.Add(AchievementCatalog.GettingStarted);
        }

        if (context.TasksCompletedTotal >= 100)
        {
            earned.Add(AchievementCatalog.Centurion);
        }

        if (context.CompletedTask.Difficulty == Difficulty.Epic)
        {
            earned.Add(AchievementCatalog.EpicSlayer);
        }

        if (context.HardOrEpicCompleted >= 10)
        {
            earned.Add(AchievementCatalog.GiantKiller);
        }

        if (context.CompletedTask.XpAwarded >= 50)
        {
            earned.Add(AchievementCatalog.DeepWork);
        }

        if (context.Level >= 5)
        {
            earned.Add(AchievementCatalog.Level5);
        }

        if (context.Level >= 10)
        {
            earned.Add(AchievementCatalog.Level10);
        }

        if (context.Level >= 25)
        {
            earned.Add(AchievementCatalog.Level25);
        }

        // Only counts as clearing the board if there was a board worth clearing.
        if (context.OpenTasksAfter == 0 && context.OpenTasksBefore >= 3)
        {
            earned.Add(AchievementCatalog.CleanSlate);
        }

        var localHour = context.LocalCompletedAt.Hour;

        if (localHour < 4)
        {
            earned.Add(AchievementCatalog.NightOwl);
        }

        if (localHour < 6)
        {
            earned.Add(AchievementCatalog.EarlyBird);
        }

        if (context.CompletedTodayLocal >= 5)
        {
            earned.Add(AchievementCatalog.ProductiveDay);
        }

        return earned;
    }
}
