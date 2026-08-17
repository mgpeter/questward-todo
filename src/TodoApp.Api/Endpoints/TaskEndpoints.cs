using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Auth;
using TodoApp.Api.Contracts;
using TodoApp.Api.Mapping;
using TodoApp.Api.Services;
using TodoApp.Api.Validation;
using TodoApp.Data;
using TodoApp.Models;
using TodoApp.Models.Progression;

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
        group.MapPut("/{id:guid}/status", SetStatus).ValidateBody<SetStatusRequest>();
        group.MapGet("/tags", GetTags);
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
        string? search = null,
        string? tag = null)
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

        // Filters describe top-level tasks. A subtask arrives with its parent or not at
        // all, because a checklist item torn out of its checklist means nothing.
        var query = db.Tasks.AsNoTracking().Where(t => t.UserId == user.Id && t.ParentId == null);

        // A recurring task whose period has rolled over is stored Completed but is open
        // again, so the filter has to ask the same question TodoTask.StatusAt asks. Written
        // out in SQL rather than filtered in memory because it decides which rows come back.
        var now = DateTimeOffset.UtcNow;

        query = status?.ToLowerInvariant() switch
        {
            "open" => query.Where(t =>
                t.Status != TaskProgress.Completed ||
                (t.Recurrence != RecurrenceRule.None &&
                 t.XpEligibleFrom != null &&
                 t.XpEligibleFrom <= now)),
            "done" => query.Where(t =>
                t.Status == TaskProgress.Completed &&
                (t.Recurrence == RecurrenceRule.None ||
                 t.XpEligibleFrom == null ||
                 t.XpEligibleFrom > now)),
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

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var wanted = tag.Trim();
            query = query.Where(t => t.Tags.Contains(wanted));
        }

        var tasks = await query.ToListAsync(cancellationToken);

        var parentIds = tasks.Select(t => t.Id).ToList();

        // Second round trip rather than an Include, so the filters above stay written
        // against parents alone and cannot accidentally match on a child's fields.
        var subtasks = await db.Tasks
            .AsNoTracking()
            .Where(t => t.UserId == user.Id && t.ParentId != null && parentIds.Contains(t.ParentId.Value))
            .ToListAsync(cancellationToken);

        var childrenByParent = subtasks
            .GroupBy(t => t.ParentId!.Value)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<TodoTask>)group
                    .OrderBy(t => t.IsCompletedAt(now))
                    .ThenBy(t => t.SortOrder)
                    .ThenBy(t => t.CreatedAt)
                    .ToList());

        // Open tasks keep their manual order; completed ones show most recently finished first.
        // Sorted in memory because the two halves want different keys, and a personal list is small.
        var ordered = tasks
            .OrderBy(t => t.IsCompletedAt(now))
            .ThenBy(t => t.IsCompletedAt(now) ? 0 : t.SortOrder)
            .ThenByDescending(t => t.CompletedAt ?? DateTimeOffset.MinValue)
            .ThenBy(t => t.CreatedAt)
            .Select(t => t.ToDto(childrenByParent.GetValueOrDefault(t.Id)))
            .ToList();

        return Results.Ok(ordered);
    }

    /// <summary>Every tag the caller has used, for autocomplete and the filter bar.</summary>
    private static async Task<IResult> GetTags(
        TodoDbContext db,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);

        var tags = await db.Tasks
            .AsNoTracking()
            .Where(t => t.UserId == user.Id)
            .Select(t => t.Tags)
            .ToListAsync(cancellationToken);

        var distinct = tags
            .SelectMany(list => list)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Results.Ok(distinct);
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
        if (task is null)
        {
            return Results.NotFound();
        }

        var subtasks = await db.Tasks
            .AsNoTracking()
            .Where(t => t.UserId == user.Id && t.ParentId == id)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        return Results.Ok(task.ToDto(subtasks));
    }

    /// <summary>
    /// Trimmed, de-duplicated case-insensitively and capped. Tags are free text typed in a
    /// hurry, so "Work", "work " and "work" have to collapse or the filter bar fills with
    /// near-duplicates that each match a different third of the list.
    /// </summary>
    private static List<string> NormalizeTags(IReadOnlyList<string>? tags) =>
        tags is null
            ? []
            : tags
                .Select(tag => tag.Trim())
                .Where(tag => tag.Length is > 0 and <= 32)
                .DistinctBy(tag => tag.ToLowerInvariant())
                .Take(10)
                .ToList();

    private static async Task<IResult> CreateTask(
        CreateTaskRequest request,
        TodoDbContext db,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);

        if (request.ParentId is { } parentId)
        {
            var parent = await db.Tasks
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == parentId && t.UserId == user.Id, cancellationToken);

            if (parent is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["parentId"] = ["That task does not exist."]
                });
            }

            // One level, deliberately. Arbitrary nesting turns a checklist into a project
            // planner, and it would put the "does this pay XP?" question on a recursive
            // walk instead of a single null check.
            if (parent.ParentId is not null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["parentId"] = ["Subtasks cannot have subtasks of their own."]
                });
            }
        }

        // New tasks land at the top of the caller's open list. Scoped, or one user's
        // sort order would drag another user's new tasks around. Subtasks are ordered
        // within their parent, so they sort against their siblings instead.
        var topSortOrder = request.ParentId is { } sortParent
            ? await db.Tasks
                .Where(t => t.UserId == user.Id && t.ParentId == sortParent)
                .Select(t => (int?)t.SortOrder)
                .MaxAsync(cancellationToken) + 1 ?? 0
            : await db.Tasks
                .Where(t => t.UserId == user.Id && t.ParentId == null && t.Status != TaskProgress.Completed)
                .Select(t => (int?)t.SortOrder)
                .MinAsync(cancellationToken) - 1 ?? 0;

        var now = DateTimeOffset.UtcNow;

        var task = new TodoTask
        {
            UserId = user.Id,
            ParentId = request.ParentId,
            Title = request.Title.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            Difficulty = request.Difficulty,
            Priority = request.Priority,
            Tags = NormalizeTags(request.Tags),
            DueDate = request.DueDate?.ToUniversalTime(),
            // A subtask pays nothing, so letting it carry a recurrence would only be a
            // setting that does nothing.
            Recurrence = request.ParentId is null ? request.Recurrence : RecurrenceRule.None,
            SortOrder = topSortOrder,
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
        task.Tags = NormalizeTags(request.Tags);
        task.DueDate = request.DueDate?.ToUniversalTime();
        task.Recurrence = task.ParentId is null ? request.Recurrence : RecurrenceRule.None;
        task.UpdatedAt = DateTimeOffset.UtcNow;

        // XpAwarded is intentionally untouched: changing difficulty after the fact
        // must not rewrite XP that was already banked. XpEligibleFrom is untouched for
        // the same reason - it is the record of a reward already taken, so switching a
        // daily task to "none" and back cannot reopen today's payout.
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(task.ToDto());
    }

    /// <summary>
    /// The board's drag target. Moving into or out of Completed routes through the
    /// gamification service rather than setting the column, because that is where XP,
    /// stamina, badges and quests are kept consistent.
    /// </summary>
    private static async Task<IResult> SetStatus(
        Guid id,
        SetStatusRequest request,
        TodoDbContext db,
        GamificationService gamification,
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

        var now = DateTimeOffset.UtcNow;

        if (request.Status == task.StatusAt(now))
        {
            var character = await gamification.GetCharacterAsync(user.Id, cancellationToken);
            var unlocked = await gamification.CountUnlockedAsync(user.Id, cancellationToken);

            return Results.Ok(new SetStatusResponse(
                task.ToDto(),
                XpDelta: 0,
                character.ToDto(unlocked),
                LeveledUp: false,
                LeveledDown: false,
                LevelCurve.LevelForXp(character.TotalXp),
                []));
        }

        if (request.Status == TaskProgress.Completed)
        {
            var completed = await gamification.CompleteAsync(
                user.Id, id, request.UtcOffsetMinutes, cancellationToken);

            return completed is null
                ? Results.NotFound()
                : Results.Ok(new SetStatusResponse(
                    completed.Task,
                    completed.XpGained,
                    completed.Character,
                    completed.LeveledUp,
                    LeveledDown: false,
                    completed.PreviousLevel,
                    completed.UnlockedAchievements));
        }

        if (task.IsCompletedAt(now))
        {
            var reopened = await gamification.ReopenAsync(user.Id, id, cancellationToken);

            if (reopened is null)
            {
                return Results.NotFound();
            }

            // Reopening lands in Todo; honour a drag that went straight to In progress.
            if (request.Status == TaskProgress.InProgress)
            {
                task.Status = TaskProgress.InProgress;
                task.StartedAt ??= DateTimeOffset.UtcNow;
                task.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.Ok(new SetStatusResponse(
                task.ToDto(),
                -reopened.XpLost,
                reopened.Character,
                LeveledUp: false,
                reopened.LeveledDown,
                reopened.PreviousLevel,
                []));
        }

        // Todo <-> InProgress: no progression is involved, so it is just the column.
        task.Status = request.Status;
        task.StartedAt = request.Status == TaskProgress.InProgress
            ? task.StartedAt ?? DateTimeOffset.UtcNow
            : null;
        task.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        var sheet = await gamification.GetCharacterAsync(user.Id, cancellationToken);
        var badges = await gamification.CountUnlockedAsync(user.Id, cancellationToken);

        return Results.Ok(new SetStatusResponse(
            task.ToDto(),
            XpDelta: 0,
            sheet.ToDto(badges),
            LeveledUp: false,
            LeveledDown: false,
            LevelCurve.LevelForXp(sheet.TotalXp),
            []));
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
