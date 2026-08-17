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

        var xpGained = task.Difficulty.BaseXp();

        task.IsCompleted = true;
        task.CompletedAt = completedAtUtc;
        task.XpAwarded = xpGained;
        task.UpdatedAt = completedAtUtc;

        character.TotalXp += xpGained;
        character.TasksCompleted += 1;

        // The RPG layer is fuelled from here and nowhere else. Stamina buys fights and
        // finishing work restores hit points, so the adventure always points back at the
        // task list (DEC-003).
        character.Stamina += task.Difficulty.Stamina();
        character.CurrentHitPoints += task.Difficulty.Stamina();
        character.HitPointsUpdatedAt = completedAtUtc;

        await db.SaveChangesAsync(cancellationToken);

        var newLevel = LevelCurve.LevelForXp(character.TotalXp);

        var context = new AchievementContext(
            CompletedTask: task,
            TasksCompletedTotal: character.TasksCompleted,
            Level: newLevel,
            HardOrEpicCompleted: await db.Tasks.CountAsync(
                t => t.UserId == userId && t.IsCompleted && t.Difficulty >= Difficulty.Hard,
                cancellationToken),
            OpenTasksAfter: await db.Tasks.CountAsync(
                t => t.UserId == userId && !t.IsCompleted,
                cancellationToken),
            CompletedTodayLocal: await db.Tasks.CountAsync(
                t => t.UserId == userId && t.IsCompleted && t.CompletedAt >= localDayStartUtc,
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

        task.IsCompleted = false;
        task.CompletedAt = null;
        task.XpAwarded = 0;
        task.UpdatedAt = DateTimeOffset.UtcNow;

        // Clamped: XP can never go negative, even if history was edited out from under us.
        character.TotalXp = Math.Max(0, character.TotalXp - xpLost);
        character.TasksCompleted = Math.Max(0, character.TasksCompleted - 1);

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
