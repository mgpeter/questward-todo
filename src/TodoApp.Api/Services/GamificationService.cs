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
    Rpg.QuestService quests)
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

        // Completing an already-complete task must never award a second time. Asked as of
        // now, so a recurring task that has come round again counts as open.
        if (task.IsCompletedAt(DateTimeOffset.UtcNow))
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
        var awards = task.MayAwardAt(completedAtUtc);

        var xpGained = awards ? task.Difficulty.BaseXp() : 0;
        var staminaGained = awards ? task.Difficulty.Stamina() : 0;

        task.Status = TaskProgress.Completed;
        task.CompletedAt = completedAtUtc;
        task.XpAwarded = xpGained;
        task.StaminaAwarded = staminaGained;
        task.UpdatedAt = completedAtUtc;

        // Move the recurrence gate forward. Monotonic: it never moves back and is never
        // cleared, so "set daily, complete, set none, complete, set daily" cannot mint XP.
        if (task.Recurrence != RecurrenceRule.None)
        {
            var next = RecurrenceRules.NextEligibleAfter(task.Recurrence, completedAtUtc);

            if (next is not null && (task.XpEligibleFrom is null || next > task.XpEligibleFrom))
            {
                task.XpEligibleFrom = next;
            }
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
        await quests.RecordAsync(userId, ObjectiveKind.CompleteTask, string.Empty, 1, cancellationToken);
        await quests.RecordAsync(
            userId, ObjectiveKind.CompleteTask, task.Difficulty.ToString(), 1, cancellationToken);
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

        if (!task.IsCompletedAt(DateTimeOffset.UtcNow))
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

        // Reopening a completion that paid hands the reward back, so the recurrence gate
        // it closed has to open again. Otherwise an accidental tick and untick destroys
        // the day's XP with no way to earn it back. Only when it actually paid: reopening
        // a within-period repeat must leave the earlier payout's gate exactly where it is.
        if (xpLost > 0)
        {
            task.XpEligibleFrom = null;
        }

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
