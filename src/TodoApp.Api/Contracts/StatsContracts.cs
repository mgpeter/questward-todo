using TodoApp.Models;

namespace TodoApp.Api.Contracts;

public sealed record DifficultyBreakdown(Difficulty Difficulty, int Completed, int XpEarned);

public sealed record DailyCompletion(DateOnly Date, int Completed, int XpEarned);

public sealed record StatsDto(
    int TotalTasks,
    int OpenTasks,
    int CompletedTasks,
    int OverdueTasks,
    int TotalXp,
    int Level,
    string Title,
    IReadOnlyList<DifficultyBreakdown> ByDifficulty,
    IReadOnlyList<DailyCompletion> Last14Days);
