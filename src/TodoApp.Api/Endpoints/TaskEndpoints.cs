using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Auth;
using TodoApp.Api.Contracts;
using TodoApp.Api.Mapping;
using TodoApp.Api.Services;
using TodoApp.Api.Services.Rpg;
using TodoApp.Api.Validation;
using TodoApp.Data;
using TodoApp.Models;
using TodoApp.Models.Progression;
using TodoApp.Models.Rpg;

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
        group.MapDelete("/completed", ClearCompleted);
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

        var now = DateTimeOffset.UtcNow;

        query = status?.ToLowerInvariant() switch
        {
            "open" => query.Where(t => t.Status != TaskProgress.Completed),
            "done" => query.Where(t => t.Status == TaskProgress.Completed),
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
                    .OrderBy(t => t.IsCompleted)
                    .ThenBy(t => t.SortOrder)
                    .ThenBy(t => t.CreatedAt)
                    .ToList());

        // Open tasks keep their manual order; completed ones show most recently finished first.
        // Sorted in memory because the two halves want different keys, and a personal list is small.
        var ordered = tasks
            .OrderBy(t => t.IsCompleted)
            .ThenBy(t => t.IsCompleted ? 0 : t.SortOrder)
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
        HuntService hunts,
        ILoggerFactory loggerFactory,
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

        if (request.Status == task.Status)
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

            if (completed is null)
            {
                return Results.NotFound();
            }

            // After CompleteAsync has returned, which is to say after its transaction has
            // committed. See DischargeHuntAsync for why that ordering is the whole feature.
            var discharged = await DischargeHuntAsync(
                hunts, loggerFactory, user.Id, id, cancellationToken);

            return Results.Ok(new SetStatusResponse(
                completed.Task,
                completed.XpGained,
                completed.Character,
                completed.LeveledUp,
                LeveledDown: false,
                completed.PreviousLevel,
                completed.UnlockedAchievements,
                discharged));
        }

        if (task.IsCompleted)
        {
            var reopened = await gamification.ReopenAsync(user.Id, id, cancellationToken);

            if (reopened is null)
            {
                return Results.NotFound();
            }

            // The same take-back the reopen route does, and it has to be here too: dragging a
            // card out of Done is the same act as pressing Reopen, and a contract that stayed
            // discharged through one of the two doors would pay a bounty on a task the player
            // has just said is not finished.
            await UndischargeHuntAsync(hunts, loggerFactory, user.Id, id, cancellationToken);

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

    /// <summary>
    /// Deletes a task, and tears up any contract that was still waiting on it.
    /// </summary>
    /// <remarks>
    /// The sweep is explicit because the referential action cannot tell the two live states apart.
    /// An accepted contract is a promise to finish this task, and deleting the task deletes the
    /// only thing that could ever discharge it, so it is torn up: nothing was spent on it, so
    /// nothing is taken. A discharged one is left alone and keeps its fight, with the foreign key
    /// nulling the link: the work was done, and tidying the row away afterwards must not take back
    /// what doing it earned.
    /// <para>
    /// One statement, ahead of the delete, and neither is inside a transaction because there is
    /// nothing to lose if the delete then fails: a contract torn up on a task that survives can be
    /// taken again for nothing.
    /// </para>
    /// </remarks>
    private static async Task<IResult> DeleteTask(
        Guid id,
        TodoDbContext db,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);

        await db.HuntContracts
            .Where(c => c.TaskId == id
                && c.UserId == user.Id
                && c.Status == HuntContractStatus.Accepted)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(c => c.Status, HuntContractStatus.Abandoned)
                    .SetProperty(c => c.ClosedAt, DateTimeOffset.UtcNow),
                cancellationToken);

        var deleted = await db.Tasks
            .Where(t => t.Id == id && t.UserId == user.Id)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    /// <summary>
    /// Deletes finished tasks older than the record can see.
    /// </summary>
    /// <remarks>
    /// Deliberately not "clear everything finished". The record panel is computed from these
    /// rows: the fourteen day activity chart reads <c>CompletedAt</c> directly and the
    /// difficulty breakdown groups them, so clearing the lot would blank the two panels that
    /// justify keeping any of it. Older than the window, a finished task contributes to nothing
    /// but the row count, which is the only thing anyone wanted rid of.
    /// <para>
    /// The floor is the stats window itself, so the two cannot drift apart: raising
    /// <c>StatsEndpoints.TrendDays</c> without raising this would start eating the chart.
    /// </para>
    /// <para>
    /// XP is not refunded and <c>Character.TasksCompleted</c> does not go down, matching what
    /// deleting a single task has always done. Both are a memory of work done rather than a
    /// balance, in the same way a badge is never revoked.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ClearCompleted(
        TodoDbContext db,
        ICurrentUser currentUser,
        CancellationToken cancellationToken,
        int olderThanDays = StatsEndpoints.TrendDays)
    {
        var user = await currentUser.GetAsync(cancellationToken);

        // Never inside the window, whatever the caller asks for. A smaller number here is the
        // one way this could quietly start deleting what the chart is drawing.
        var days = Math.Max(StatsEndpoints.TrendDays, olderThanDays);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);

        // Subtasks go with their parents by cascade, so only top-level rows are matched. A
        // subtask whose parent is still open is part of live work and is not swept up by it.
        var doomed = await db.Tasks
            .Where(t => t.UserId == user.Id
                && t.ParentId == null
                && t.Status == TaskProgress.Completed
                && t.CompletedAt != null
                && t.CompletedAt < cutoff)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        if (doomed.Count == 0)
        {
            return Results.Ok(new ClearCompletedResponse(0, days));
        }

        // The same courtesy DeleteTask does: a contract still waiting on a task that is about
        // to stop existing is torn up rather than left pointing at nothing.
        await db.HuntContracts
            .Where(c => c.UserId == user.Id
                && c.TaskId != null
                && doomed.Contains(c.TaskId.Value)
                && c.Status == HuntContractStatus.Accepted)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(c => c.Status, HuntContractStatus.Abandoned)
                    .SetProperty(c => c.ClosedAt, DateTimeOffset.UtcNow),
                cancellationToken);

        var deleted = await db.Tasks
            .Where(t => doomed.Contains(t.Id))
            .ExecuteDeleteAsync(cancellationToken);

        return Results.Ok(new ClearCompletedResponse(deleted, days));
    }

    private static async Task<IResult> CompleteTask(
        Guid id,
        GamificationService gamification,
        HuntService hunts,
        ILoggerFactory loggerFactory,
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

        if (result is null)
        {
            return Results.NotFound();
        }

        // After CompleteAsync has returned, which is to say after its transaction has committed.
        // See DischargeHuntAsync for why that ordering is the whole feature.
        //
        // Reached on every completion, the already-complete no-op included: that branch opens no
        // transaction at all, so pressing Done a second time is a free retry of a discharge that
        // failed the first time. It cannot pay twice, or pay for the wrong window: discharging
        // moves no gold at all, and it is refused unless the completion on the row postdates the
        // contract.
        var discharged = await DischargeHuntAsync(
            hunts, loggerFactory, user.Id, id, cancellationToken);

        return Results.Ok(discharged is null ? result : result with { Hunt = discharged });
    }

    /// <summary>
    /// Discharges the contract on a task that has just been completed, if there is one.
    /// </summary>
    /// <remarks>
    /// The ordering rule of the whole hunt feature, and the reason this lives here rather than
    /// inside <see cref="GamificationService"/>.
    /// <para>
    /// CompleteAsync owns the only explicit transaction in the tree. It commits, and only then
    /// returns. By the time this method is reached, the <c>await using var transaction</c> inside
    /// it has already been disposed, so nothing below can roll a completion back: a contract that
    /// fails to discharge can cost the player the contract, never the XP, stamina, badges or quest
    /// progress they earned by doing the work.
    /// </para>
    /// <para>
    /// The ordering is structural rather than disciplinary. HuntService is not injected into
    /// GamificationService and never should be, so there is no field, no constructor parameter
    /// and no using through which a later edit could pull hunt work up above the commit. That is
    /// also what disarms the shared context trap: <c>db</c> is one scoped TodoDbContext and its
    /// SaveChangesAsync flushes the whole change tracker, so a hunt entity tracked at any point
    /// before the commit would land inside the XP transaction however innocent the call site
    /// looked. Nothing hunt-shaped is ever tracked before this line, because this line is the
    /// first mention of a hunt in the request.
    /// </para>
    /// <para>
    /// The catch is the fallback and not the mechanism. The boundary above is what guarantees the
    /// completion survives; this only stops a failed discharge turning a successful completion
    /// into a 500. Nothing is lost by one: discharging pays nothing, and pressing Done again
    /// retries it for free.
    /// </para>
    /// </remarks>
    private static async Task<HuntContractDto?> DischargeHuntAsync(
        HuntService hunts,
        ILoggerFactory loggerFactory,
        Guid userId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        try
        {
            var discharged = await hunts.DischargeAsync(userId, taskId, cancellationToken);

            return discharged?.ToDto();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            loggerFactory
                .CreateLogger(typeof(TaskEndpoints))
                .LogError(
                    exception,
                    "Discharging the contract on task {TaskId} for user {UserId} failed after the "
                    + "completion had already committed. The completion stands. The contract is "
                    + "still accepted and discharges on the next press of Done.",
                    taskId,
                    userId);

            return null;
        }
    }

    /// <summary>
    /// Reopens a task, and takes back the discharge on any contract it had earned.
    /// </summary>
    /// <remarks>
    /// A discharged contract is the record of work that was done. Reopening says it was not, so
    /// the contract goes back to accepted and waits to be earned again. Without this, the loop
    /// "finish it, undo it, collect the bounty" would pay gold, loot and standing on a task that
    /// ends the sequence unfinished, which is the shape DEC-013 exists to refuse.
    /// <para>
    /// A contract already fought is untouched, matching how a badge, a quest advance and spent
    /// stamina all survive a reopen: the fight happened, and the chronicle does not un-happen.
    /// </para>
    /// <para>
    /// Runs after ReopenAsync has returned, for the reason
    /// <see cref="DischargeHuntAsync"/> spells out at length: the completion path owns the only
    /// explicit transaction in the tree, and no hunt work may be tracked before it commits.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ReopenTask(
        Guid id,
        GamificationService gamification,
        HuntService hunts,
        ILoggerFactory loggerFactory,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await gamification.ReopenAsync(user.Id, id, cancellationToken);

        if (result is null)
        {
            return Results.NotFound();
        }

        await UndischargeHuntAsync(hunts, loggerFactory, user.Id, id, cancellationToken);

        return Results.Ok(result);
    }

    /// <inheritdoc cref="ReopenTask"/>
    private static async Task UndischargeHuntAsync(
        HuntService hunts,
        ILoggerFactory loggerFactory,
        Guid userId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        try
        {
            await hunts.UndischargeAsync(userId, taskId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            loggerFactory
                .CreateLogger(typeof(TaskEndpoints))
                .LogError(
                    exception,
                    "Taking back the discharge on task {TaskId} for user {UserId} failed after the "
                    + "reopen had already committed. The reopen stands. The contract is still "
                    + "discharged and can be fought once.",
                    taskId,
                    userId);
        }
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
