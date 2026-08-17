using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Mapping;
using TodoApp.Data;
using TodoApp.Models.Progression;

namespace TodoApp.Api.Endpoints;

public static class AchievementEndpoints
{
    public static IEndpointRouteBuilder MapAchievementEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/achievements", GetAchievements).WithTags("Achievements");

        return app;
    }

    private static async Task<IResult> GetAchievements(TodoDbContext db, CancellationToken cancellationToken)
    {
        var unlocks = await db.AchievementUnlocks
            .AsNoTracking()
            .ToDictionaryAsync(a => a.AchievementKey, a => a.UnlockedAt, cancellationToken);

        // The catalog drives the order, so the badge grid stays stable as more are earned.
        var achievements = AchievementCatalog.All
            .Select(definition => definition.ToDto(
                unlocks.TryGetValue(definition.Key, out var unlockedAt) ? unlockedAt : null))
            .ToList();

        return Results.Ok(achievements);
    }
}
