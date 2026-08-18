using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Auth;
using TodoApp.Api.Contracts;
using TodoApp.Api.Services;
using TodoApp.Data;
using TodoApp.Models;
using TodoApp.Models.Progression;

namespace TodoApp.Api.Endpoints;

public static class StatsEndpoints
{
    private const int TrendDays = 14;

    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stats", GetStats)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.PerUser)
            .WithTags("Stats");

        return app;
    }

    private static async Task<IResult> GetStats(
        TodoDbContext db,
        GamificationService gamification,
        ICurrentUser currentUser,
        CancellationToken cancellationToken,
        int utcOffsetMinutes = 0)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var character = await gamification.GetCharacterAsync(user.Id, cancellationToken);
        var progress = LevelCurve.Describe(character.TotalXp);

        var offset = TimeSpan.FromMinutes(Math.Clamp(utcOffsetMinutes, -840, 840));
        var localNow = DateTimeOffset.UtcNow.ToOffset(offset);
        var today = DateOnly.FromDateTime(localNow.Date);
        var windowStartUtc = new DateTimeOffset(localNow.Date.AddDays(-(TrendDays - 1)), offset)
            .ToUniversalTime();

        // Subtasks are excluded here, at the source, so every aggregate below inherits it.
        // Without this the record read "3 completed, Epic 3 - 200 XP" while the character
        // card beside it read "2 done": a checklist item was being counted as a finished
        // task on one panel and not the other (DEC-014).
        var mine = db.Tasks.Where(t => t.UserId == user.Id && t.ParentId == null);

        var now = DateTimeOffset.UtcNow;

        var totalTasks = await mine.CountAsync(cancellationToken);
        var completedTasks = await mine.CountAsync(t => t.Status == TaskProgress.Completed, cancellationToken);

        var open = mine.Where(t => t.Status != TaskProgress.Completed);

        var openTasks = await open.CountAsync(cancellationToken);
        var overdueTasks = await open.CountAsync(
            t => t.DueDate != null && t.DueDate < now, cancellationToken);

        var byDifficulty = await mine
            .Where(t => t.Status == TaskProgress.Completed)
            .GroupBy(t => t.Difficulty)
            .Select(g => new DifficultyBreakdown(g.Key, g.Count(), g.Sum(t => t.XpAwarded)))
            .ToListAsync(cancellationToken);

        // Every difficulty appears, including the ones never used, so the chart has a stable shape.
        var breakdown = Enum.GetValues<Difficulty>()
            .Select(difficulty =>
                byDifficulty.FirstOrDefault(b => b.Difficulty == difficulty)
                ?? new DifficultyBreakdown(difficulty, 0, 0))
            .ToList();

        var recent = await mine
            .AsNoTracking()
            .Where(t => t.Status == TaskProgress.Completed && t.CompletedAt >= windowStartUtc)
            .Select(t => new { t.CompletedAt, t.XpAwarded })
            .ToListAsync(cancellationToken);

        var grouped = recent
            .GroupBy(t => DateOnly.FromDateTime(t.CompletedAt!.Value.ToOffset(offset).Date))
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Xp: g.Sum(t => t.XpAwarded)));

        var trend = Enumerable.Range(0, TrendDays)
            .Select(dayOffset =>
            {
                var date = today.AddDays(-(TrendDays - 1 - dayOffset));
                return grouped.TryGetValue(date, out var entry)
                    ? new DailyCompletion(date, entry.Count, entry.Xp)
                    : new DailyCompletion(date, 0, 0);
            })
            .ToList();

        return Results.Ok(new StatsDto(
            totalTasks,
            openTasks,
            completedTasks,
            overdueTasks,
            progress.TotalXp,
            progress.Level,
            progress.Title,
            breakdown,
            trend));
    }
}
