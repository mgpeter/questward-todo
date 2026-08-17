using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Auth;
using TodoApp.Api.Contracts;
using TodoApp.Api.Mapping;
using TodoApp.Api.Services;
using TodoApp.Api.Validation;
using TodoApp.Data;
using TodoApp.Models;

namespace TodoApp.Api.Endpoints;

public static class TaskEndpoints
{
    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tasks")
            .WithTags("Tasks")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.PerUser);

        group.MapGet("/", GetTasks);
        group.MapGet("/{id:guid}", GetTask);
        group.MapPost("/", CreateTask).ValidateBody<CreateTaskRequest>();
        group.MapPut("/{id:guid}", UpdateTask).ValidateBody<UpdateTaskRequest>();
        group.MapDelete("/{id:guid}", DeleteTask);
        group.MapPost("/{id:guid}/complete", CompleteTask);
        group.MapPost("/{id:guid}/reopen", ReopenTask);
        group.MapPost("/reorder", ReorderTasks).ValidateBody<ReorderRequest>();

        return app;
    }

    private static async Task<IResult> GetTasks(
        TodoDbContext db,
        ICurrentUser currentUser,
        CancellationToken cancellationToken,
        string? status = "all",
        // Bound as a string rather than Difficulty? on purpose. Minimal API enum binding
        // is case-sensitive, so "?difficulty=epic" from the client 400s while only
        // "?difficulty=Epic" works. Parsed case-insensitively below instead.
        string? difficulty = null,
        string? search = null)
    {
        var user = await currentUser.GetAsync(cancellationToken);

        Difficulty? difficultyFilter = null;

        if (!string.IsNullOrWhiteSpace(difficulty))
        {
            if (!Enum.TryParse<Difficulty>(difficulty, ignoreCase: true, out var parsed))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["difficulty"] = [$"'{difficulty}' is not a valid difficulty."]
                });
            }

            difficultyFilter = parsed;
        }

        var query = db.Tasks.AsNoTracking().Where(t => t.UserId == user.Id);

        query = status?.ToLowerInvariant() switch
        {
            "open" => query.Where(t => !t.IsCompleted),
            "done" => query.Where(t => t.IsCompleted),
            _ => query
        };

        if (difficultyFilter.HasValue)
        {
            query = query.Where(t => t.Difficulty == difficultyFilter.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(t =>
                EF.Functions.ILike(t.Title, term) ||
                (t.Notes != null && EF.Functions.ILike(t.Notes, term)));
        }

        var tasks = await query.ToListAsync(cancellationToken);

        // Open tasks keep their manual order; completed ones show most recently finished first.
        // Sorted in memory because the two halves want different keys, and a personal list is small.
        var ordered = tasks
            .OrderBy(t => t.IsCompleted)
            .ThenBy(t => t.IsCompleted ? 0 : t.SortOrder)
            .ThenByDescending(t => t.CompletedAt ?? DateTimeOffset.MinValue)
            .ThenBy(t => t.CreatedAt)
            .Select(t => t.ToDto())
            .ToList();

        return Results.Ok(ordered);
    }

    private static async Task<IResult> GetTask(
        Guid id,
        TodoDbContext db,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);

        var task = await db.Tasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == user.Id, cancellationToken);

        // 404 rather than 403 for someone else's task, so ids cannot be probed.
        return task is null ? Results.NotFound() : Results.Ok(task.ToDto());
    }

    private static async Task<IResult> CreateTask(
        CreateTaskRequest request,
        TodoDbContext db,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);

        // New tasks land at the top of the caller's open list. Scoped, or one user's
        // sort order would drag another user's new tasks around.
        var topSortOrder = await db.Tasks
            .Where(t => t.UserId == user.Id && !t.IsCompleted)
            .Select(t => (int?)t.SortOrder)
            .MinAsync(cancellationToken) ?? 0;

        var now = DateTimeOffset.UtcNow;

        var task = new TodoTask
        {
            UserId = user.Id,
            Title = request.Title.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            Difficulty = request.Difficulty,
            Priority = request.Priority,
            DueDate = request.DueDate?.ToUniversalTime(),
            SortOrder = topSortOrder - 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/tasks/{task.Id}", task.ToDto());
    }

    private static async Task<IResult> UpdateTask(
        Guid id,
        UpdateTaskRequest request,
        TodoDbContext db,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);

        var task = await db.Tasks
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == user.Id, cancellationToken);

        if (task is null)
        {
            return Results.NotFound();
        }

        task.Title = request.Title.Trim();
        task.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        task.Difficulty = request.Difficulty;
        task.Priority = request.Priority;
        task.DueDate = request.DueDate?.ToUniversalTime();
        task.UpdatedAt = DateTimeOffset.UtcNow;

        // XpAwarded is intentionally untouched: changing difficulty after the fact
        // must not rewrite XP that was already banked.
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(task.ToDto());
    }

    private static async Task<IResult> DeleteTask(
        Guid id,
        TodoDbContext db,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);

        var deleted = await db.Tasks
            .Where(t => t.Id == id && t.UserId == user.Id)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> CompleteTask(
        Guid id,
        GamificationService gamification,
        ICurrentUser currentUser,
        CancellationToken cancellationToken,
        CompleteTaskRequest? request = null)
    {
        var user = await currentUser.GetAsync(cancellationToken);

        var result = await gamification.CompleteAsync(
            user.Id,
            id,
            request?.UtcOffsetMinutes ?? 0,
            cancellationToken);

        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> ReopenTask(
        Guid id,
        GamificationService gamification,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await gamification.ReopenAsync(user.Id, id, cancellationToken);

        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> ReorderTasks(
        ReorderRequest request,
        TodoDbContext db,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var ids = request.OrderedIds.ToList();

        // Ids belonging to another user simply do not come back, so they are skipped the
        // same way unknown ids always were rather than failing the whole batch.
        var tasks = await db.Tasks
            .Where(t => t.UserId == user.Id && ids.Contains(t.Id))
            .ToListAsync(cancellationToken);

        var byId = tasks.ToDictionary(t => t.Id);
        var now = DateTimeOffset.UtcNow;

        for (var index = 0; index < ids.Count; index++)
        {
            if (!byId.TryGetValue(ids[index], out var task))
            {
                continue;
            }

            task.SortOrder = index;
            task.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}
