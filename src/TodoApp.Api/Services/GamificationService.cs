using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Contracts;
using TodoApp.Api.Mapping;
using TodoApp.Data;
using TodoApp.Models;
using TodoApp.Models.Progression;
using TodoApp.Models.Rpg;

namespace TodoApp.Api.Services;

/// <summary>
/// Owns every mutation that can move the XP needle. Completion and reopening both run in a
/// transaction so the task, the character total and the badge rows can never drift apart.
/// </summary>
/// <remarks>
/// Every read here is scoped by <c>userId</c>. Missing one does not fail loudly, it
/// silently lets one user's activity unlock another user's badges, so the scoping is
/// covered by isolation tests rather than left to review.
/// </remarks>
public sealed class GamificationService(
    TodoDbContext db,
    AchievementEvaluator evaluator,
    Rpg.QuestService quests,
    Rpg.ChronicleService chronicle)
{
    public async Task<Character> GetCharacterAsync(Guid userId, CancellationToken cancellationToken)
    {
        var character = await db.Characters
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (character is not null)
        {
            return character;
        }

        // Provisioning creates the character alongside the user, so reaching here means
        // something went wrong upstream. Recreate rather than throw at the request.
        character = new Character { UserId = userId };
        db.Characters.Add(character);
        await db.SaveChangesAsync(cancellationToken);

        return character;
    }

    public Task<int> CountUnlockedAsync(Guid userId, CancellationToken cancellationToken) =>
        db.AchievementUnlocks.CountAsync(a => a.UserId == userId, cancellationToken);

    public async Task<CompleteTaskResponse?> CompleteAsync(
        Guid userId,
        Guid taskId,
        int utcOffsetMinutes,
        CancellationToken cancellationToken)
    {
        var task = await db.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, cancellationToken);

        if (task is null)
        {
            return null;
        }

        var character = await GetCharacterAsync(userId, cancellationToken);
        var previousLevel = LevelCurve.LevelForXp(character.TotalXp);

        // Completing an already-complete task must never award a second time.
        if (task.IsCompleted)
        {
            var unlockedCount = await CountUnlockedAsync(userId, cancellationToken);

            return new CompleteTaskResponse(
                task.ToDto(),
                XpGained: 0,
                character.ToDto(unlockedCount),
                LeveledUp: false,
                previousLevel,
                []);
        }

        var offset = TimeSpan.FromMinutes(utcOffsetMinutes);
        var completedAtUtc = DateTimeOffset.UtcNow;
        var localCompletedAt = completedAtUtc.ToOffset(offset);
        var localDayStartUtc = new DateTimeOffset(localCompletedAt.Date, offset).ToUniversalTime();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // THE progression gate, asked once and obeyed everywhere below.
        //
        // It is a single question on purpose. The alternative was repeating the same two
        // conditions across the XP branch, three achievement counts, two quest recordings
        // and four stats aggregates, where one missed filter leaks in silence: a quest
        // paying gold for twenty subtask completions breaches DEC-012 through the side
        // door, and no XP test would catch it.
        //
        // False for a subtask (splitting one task into twenty must not multiply its
        // reward) and for a recurring task completed again inside its own period.
        var awards = task.IsProgressionBearing;

        var xpGained = awards ? task.Difficulty.BaseXp() : 0;
        var staminaGained = awards ? task.Difficulty.Stamina() : 0;

        task.Status = TaskProgress.Completed;
        task.CompletedAt = completedAtUtc;
        task.XpAwarded = xpGained;
        task.StaminaAwarded = staminaGained;
        task.UpdatedAt = completedAtUtc;

        // A repeat produces the next occurrence, rather than this row quietly reappearing.
        //
        // The due date is what actually moves: anchored on the previous due date so a weekly
        // task due on Mondays stays due on Mondays however late it is ticked, and on the
        // completion only when there was no due date to anchor to. Getting that wrong is what
        // made a faithfully-kept daily report itself a year overdue (DEC-015).
        //
        // Only a progression-bearing task spawns. A subtask that repeats would breed inside its
        // parent's checklist, and a subtask cannot carry a recurrence anyway.
        var successor = awards ? SpawnSuccessor(task, completedAtUtc) : null;

        if (successor is not null)
        {
            db.Tasks.Add(successor);
            task.SpawnedTaskId = successor.Id;
        }

        character.TotalXp += xpGained;
        character.TasksCompleted += awards ? 1 : 0;

        // The RPG layer is fuelled from here and nowhere else. Stamina buys fights and
        // finishing work restores hit points, so the adventure always points back at the
        // task list (DEC-003).
        character.Stamina += staminaGained;
        character.CurrentHitPoints += staminaGained;
        character.HitPointsUpdatedAt = completedAtUtc;

        await db.SaveChangesAsync(cancellationToken);

        if (!awards)
        {
            // Completed, but it pays nothing: no XP, no stamina, no badges, no quest
            // progress. Returning early is what keeps that guarantee in one place.
            await transaction.CommitAsync(cancellationToken);

            return new CompleteTaskResponse(
                task.ToDto(),
                XpGained: 0,
                character.ToDto(await CountUnlockedAsync(userId, cancellationToken)),
                LeveledUp: false,
                previousLevel,
                []);
        }

        var newLevel = LevelCurve.LevelForXp(character.TotalXp);

        var context = new AchievementContext(
            CompletedTask: task,
            TasksCompletedTotal: character.TasksCompleted,
            Level: newLevel,
            HardOrEpicCompleted: await db.Tasks.CountAsync(
                t => t.UserId == userId && t.ParentId == null && t.Status == TaskProgress.Completed && t.Difficulty >= Difficulty.Hard,
                cancellationToken),
            OpenTasksAfter: await db.Tasks.CountAsync(
                t => t.UserId == userId && t.ParentId == null && t.Status != TaskProgress.Completed,
                cancellationToken),
            CompletedTodayLocal: await db.Tasks.CountAsync(
                t => t.UserId == userId && t.ParentId == null && t.Status == TaskProgress.Completed && t.CompletedAt >= localDayStartUtc,
                cancellationToken),
            LocalCompletedAt: localCompletedAt);

        var unlocked = await PersistNewUnlocksAsync(
            userId,
            evaluator.Evaluate(context),
            completedAtUtc,
            cancellationToken);

        // Quest objectives that count real work, recorded inside the same transaction so
        // a task and its quest progress can never disagree.
        //
        // Once, with the difficulty, and once is the whole of it. QuestService.Matches already
        // treats an empty objective target as a wildcard that matches any recorded target, so the
        // second call this used to make advanced "complete five tasks" twice for one task and
        // finished it in three.
        await quests.RecordAsync(
            userId, ObjectiveKind.CompleteTask, task.Difficulty.ToString(), 1, cancellationToken);

        // A level is the one thing in the journal that real work alone can produce, so it is
        // written where the work is counted and inside the same transaction. One line per level
        // crossed rather than one per completion: the crossing is the event.
        if (newLevel > previousLevel)
        {
            chronicle.Record(
                character,
                ChronicleKind.LevelReached,
                new Dictionary<string, string>
                {
                    [ChronicleNarrator.LevelKey] = newLevel.ToString()
                },
                at: completedAtUtc);
        }

        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var totalUnlocked = await CountUnlockedAsync(userId, cancellationToken);

        return new CompleteTaskResponse(
            task.ToDto(),
            xpGained,
            character.ToDto(totalUnlocked),
            LeveledUp: newLevel > previousLevel,
            previousLevel,
            unlocked);
    }

    public async Task<ReopenTaskResponse?> ReopenAsync(
        Guid userId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var task = await db.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, cancellationToken);

        if (task is null)
        {
            return null;
        }

        var character = await GetCharacterAsync(userId, cancellationToken);
        var previousLevel = LevelCurve.LevelForXp(character.TotalXp);

        if (!task.IsCompleted)
        {
            var unlockedCount = await CountUnlockedAsync(userId, cancellationToken);

            return new ReopenTaskResponse(
                task.ToDto(),
                XpLost: 0,
                character.ToDto(unlockedCount),
                LeveledDown: false,
                previousLevel);
        }

        var xpLost = task.XpAwarded;
        var staminaLost = task.StaminaAwarded;

        task.Status = TaskProgress.Todo;
        task.CompletedAt = null;
        task.XpAwarded = 0;
        task.StaminaAwarded = 0;
        task.UpdatedAt = DateTimeOffset.UtcNow;

        // Take the successor back with it, so an accidental tick and untick does not leave a
        // task behind. Only if nobody has touched it: once somebody has started the next
        // occurrence, or ticked something off inside it, it is work in progress and deleting
        // it would throw that away to undo an unrelated click.
        await RemoveUntouchedSuccessorAsync(task, cancellationToken);

        // Clamped: XP can never go negative, even if history was edited out from under us.
        character.TotalXp = Math.Max(0, character.TotalXp - xpLost);
        character.TasksCompleted = Math.Max(0, character.TasksCompleted - 1);

        // Stamina and hit points come back out too. Refunding XP while keeping the stamina
        // made a complete/reopen loop an unbounded source of fights, and therefore of gold
        // and loot, from no work at all.
        character.Stamina = Math.Max(0, character.Stamina - staminaLost);

        // Left standing on at least one hit point: reopening a task should never be able
        // to kill a character.
        character.CurrentHitPoints = Math.Max(1, character.CurrentHitPoints - staminaLost);
        character.HitPointsUpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        var newLevel = LevelCurve.LevelForXp(character.TotalXp);
        var totalUnlocked = await CountUnlockedAsync(userId, cancellationToken);

        // Badges are deliberately never revoked - unlocking is a memory, not a balance.
        return new ReopenTaskResponse(
            task.ToDto(),
            xpLost,
            character.ToDto(totalUnlocked),
            LeveledDown: newLevel < previousLevel,
            previousLevel);
    }

    /// <summary>
    /// The next occurrence of a repeating task, or null when it does not repeat.
    /// </summary>
    /// <remarks>
    /// A copy rather than a reset, so the finished row stays in Done as the record of work
    /// actually done and the new one starts clean. Subtask titles come across because a
    /// checklist is part of what the task is; their completions do not, because those are the
    /// part that was done.
    /// </remarks>
    private static TodoTask? SpawnSuccessor(TodoTask task, DateTimeOffset completedAt)
    {
        if (task.Recurrence == RecurrenceRule.None)
        {
            return null;
        }

        // Anchored on the due date where there is one. Anchoring everything on the completion
        // would let a weekly task walk forward through the week every time it was ticked late.
        var anchor = task.DueDate ?? completedAt;
        var next = RecurrenceRules.Advance(task.Recurrence, anchor);

        if (next is null)
        {
            return null;
        }

        // A due date that has already gone by helps nobody: ticking off a month of missed
        // dailies in one sitting should leave one task due tomorrow, not thirty in the past.
        while (next <= completedAt)
        {
            next = RecurrenceRules.Advance(task.Recurrence, next.Value);
        }

        return new TodoTask
        {
            UserId = task.UserId,
            Title = task.Title,
            Notes = task.Notes,
            Difficulty = task.Difficulty,
            Priority = task.Priority,
            Tags = [.. task.Tags],
            DueDate = next,
            Recurrence = task.Recurrence,
            SortOrder = task.SortOrder,
            CreatedAt = completedAt,
            UpdatedAt = completedAt
        };
    }

    /// <summary>
    /// Deletes the successor a completion spawned, if nobody has touched it yet.
    /// </summary>
    private async Task RemoveUntouchedSuccessorAsync(TodoTask task, CancellationToken cancellationToken)
    {
        if (task.SpawnedTaskId is not { } successorId)
        {
            return;
        }

        // Cleared either way. If the successor is kept because it has been started, the link
        // has done its job and leaving it would make a later reopen delete a task that by then
        // belongs to a different completion.
        task.SpawnedTaskId = null;

        var successor = await db.Tasks
            .FirstOrDefaultAsync(t => t.Id == successorId && t.UserId == task.UserId, cancellationToken);

        if (successor is null || successor.Status != TaskProgress.Todo || successor.StartedAt is not null)
        {
            return;
        }

        var touched = await db.Tasks.AnyAsync(
            t => t.ParentId == successorId && t.Status != TaskProgress.Todo, cancellationToken);

        if (touched)
        {
            return;
        }

        // Its own subtasks go with it. They were copied from the parent's checklist and have
        // no meaning without it.
        await db.Tasks
            .Where(t => t.ParentId == successorId)
            .ExecuteDeleteAsync(cancellationToken);

        db.Tasks.Remove(successor);
    }

    private async Task<IReadOnlyList<AchievementDto>> PersistNewUnlocksAsync(
        Guid userId,
        IReadOnlyList<string> earnedKeys,
        DateTimeOffset unlockedAt,
        CancellationToken cancellationToken)
    {
        if (earnedKeys.Count == 0)
        {
            return [];
        }

        var alreadyUnlocked = await db.AchievementUnlocks
            .Where(a => a.UserId == userId && earnedKeys.Contains(a.AchievementKey))
            .Select(a => a.AchievementKey)
            .ToListAsync(cancellationToken);

        var fresh = earnedKeys
            .Except(alreadyUnlocked, StringComparer.Ordinal)
            .ToList();

        if (fresh.Count == 0)
        {
            return [];
        }

        db.AchievementUnlocks.AddRange(fresh.Select(key => new AchievementUnlock
        {
            UserId = userId,
            AchievementKey = key,
            UnlockedAt = unlockedAt
        }));

        await db.SaveChangesAsync(cancellationToken);

        return fresh
            .Select(AchievementCatalog.Find)
            .Where(definition => definition is not null)
            .Select(definition => definition!.ToDto(unlockedAt))
            .ToList();
    }
}
