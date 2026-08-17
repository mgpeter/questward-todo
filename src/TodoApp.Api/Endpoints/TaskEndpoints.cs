using Microsoft.EntityFrameworkCore;
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
        var group = app.MapGroup("/api/tasks").WithTags("Tasks");

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
        CancellationToken cancellationToken,
        string? status = "all",
        Difficulty? difficulty = null,
        string? search = null)
    {
        var query = db.Tasks.AsNoTracking();

        query = status?.ToLowerInvariant() switch
        {
            "open" => query.Where(t => !t.IsCompleted),
            "done" => query.Where(t => t.IsCompleted),
            _ => query
        };

        if (difficulty.HasValue)
        {
            query = query.Where(t => t.Difficulty == difficulty.Value);
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

    private static async Task<IResult> GetTask(Guid id, TodoDbContext db, CancellationToken cancellationToken)
    {
        var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        return task is null ? Results.NotFound() : Results.Ok(task.ToDto());
    }

    private static async Task<IResult> CreateTask(
        CreateTaskRequest request,
        TodoDbContext db,
        CancellationToken cancellationToken)
    {
        // New tasks land at the top of the open list.
        var topSortOrder = await db.Tasks
            .Where(t => !t.IsCompleted)
            .Select(t => (int?)t.SortOrder)
            .MinAsync(cancellationToken) ?? 0;

        var now = DateTimeOffset.UtcNow;

        var task = new TodoTask
        {
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
        CancellationToken cancellationToken)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

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

    private static async Task<IResult> DeleteTask(Guid id, TodoDbContext db, CancellationToken cancellationToken)
    {
        var deleted = await db.Tasks
            .Where(t => t.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> CompleteTask(
        Guid id,
        GamificationService gamification,
        CancellationToken cancellationToken,
        CompleteTaskRequest? request = null)
    {
        var result = await gamification.CompleteAsync(
            id,
            request?.UtcOffsetMinutes ?? 0,
            cancellationToken);

        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> ReopenTask(
        Guid id,
        GamificationService gamification,
        CancellationToken cancellationToken)
    {
        var result = await gamification.ReopenAsync(id, cancellationToken);

        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> ReorderTasks(
        ReorderRequest request,
        TodoDbContext db,
        CancellationToken cancellationToken)
    {
        var ids = request.OrderedIds.ToList();

        var tasks = await db.Tasks
            .Where(t => ids.Contains(t.Id))
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
