using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Models.Progression;
using TodoApp.Models.Rpg;

namespace TodoApp.Api.Services.Rpg;

public sealed record QuestObjectiveView(string Id, string Description, int Current, int Required)
{
    public bool IsComplete => Current >= Required;
}

public sealed record QuestView(
    string Key,
    string Name,
    string Description,
    IReadOnlyList<QuestObjectiveView> Objectives,
    bool IsComplete,
    DateTimeOffset? ClaimedAt,
    int RewardGold,
    string? RewardItemKey,
    string? RewardItemName,
    /// <summary>Above the character's level. Shown rather than hidden, so there is something to aim for.</summary>
    bool IsLocked,
    int MinimumLevel);

public sealed record QuestAdvance(string Key, string Name, string Progress, bool JustCompleted);

public sealed record QuestClaim(int GoldGained, int Gold, InventoryItem? Item);

/// <summary>
/// Counts real events against the code-held quest catalog.
/// </summary>
/// <remarks>
/// Quests pay gold and items and never XP. A quest that granted experience would make the
/// todo list optional, which is the one thing the whole design is arranged to prevent
/// (DEC-003).
/// </remarks>
public sealed class QuestService(TodoDbContext db, LootService loot)
{
    /// <summary>
    /// Records progress toward every objective matching the event. Does not save; the
    /// caller commits, so a fight and its quest progress land in one transaction.
    /// </summary>
    public async Task<IReadOnlyList<QuestAdvance>> RecordAsync(
        Guid userId,
        ObjectiveKind kind,
        string target,
        int amount,
        CancellationToken cancellationToken)
    {
        if (amount <= 0)
        {
            return [];
        }

        var level = await CharacterLevelAsync(userId, cancellationToken);
        var relevant = QuestCatalog.AvailableAt(level)
            .Where(q => q.Objectives.Any(o => Matches(o, kind, target)))
            .ToList();

        if (relevant.Count == 0)
        {
            return [];
        }

        var keys = relevant.Select(q => q.Key).ToList();

        var existing = await db.QuestProgress
            .Where(p => p.UserId == userId && keys.Contains(p.QuestKey))
            .ToDictionaryAsync(p => p.QuestKey, cancellationToken);

        // Rows added by an earlier call in this same unit of work are not visible to the
        // query above, and completing a task records against two objectives in a row.
        // Without this overlay the second call inserts a duplicate and the unique index
        // rejects the whole transaction.
        foreach (var pending in db.QuestProgress.Local
                     .Where(p => p.UserId == userId && keys.Contains(p.QuestKey)))
        {
            existing[pending.QuestKey] = pending;
        }

        var advances = new List<QuestAdvance>();

        foreach (var quest in relevant)
        {
            if (!existing.TryGetValue(quest.Key, out var progress))
            {
                progress = new QuestProgress { UserId = userId, QuestKey = quest.Key };
                db.QuestProgress.Add(progress);
                existing[quest.Key] = progress;
            }

            if (progress.ClaimedAt is not null)
            {
                continue;
            }

            var counters = ReadCounters(progress);
            var wasComplete = IsComplete(quest, counters);
            var changed = false;

            foreach (var objective in quest.Objectives.Where(o => Matches(o, kind, target)))
            {
                var current = counters.GetValueOrDefault(objective.Id);

                if (current >= objective.Required)
                {
                    continue;
                }

                // Clamped so an overshoot cannot make a later objective look further along
                // than it is.
                counters[objective.Id] = Math.Min(objective.Required, current + amount);
                changed = true;
            }

            if (!changed)
            {
                continue;
            }

            WriteCounters(progress, counters);

            var nowComplete = IsComplete(quest, counters);

            advances.Add(new QuestAdvance(
                quest.Key,
                quest.Name,
                Summarise(quest, counters),
                JustCompleted: nowComplete && !wasComplete));
        }

        return advances;
    }

    public async Task<IReadOnlyList<QuestView>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var level = await CharacterLevelAsync(userId, cancellationToken);

        var progress = await db.QuestProgress
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.QuestKey, cancellationToken);

        // The whole catalog, not just what is unlocked. A quest you cannot take yet is
        // still a reason to keep going; hiding it leaves the board looking finished.
        return QuestCatalog.All
            .Select(quest =>
            {
                var counters = progress.TryGetValue(quest.Key, out var row)
                    ? ReadCounters(row)
                    : [];

                return new QuestView(
                    quest.Key,
                    quest.Name,
                    quest.Description,
                    quest.Objectives
                        .Select(o => new QuestObjectiveView(
                            o.Id, o.Description, counters.GetValueOrDefault(o.Id), o.Required))
                        .ToList(),
                    IsComplete(quest, counters),
                    row?.ClaimedAt,
                    quest.RewardGold,
                    quest.RewardItemKey,
                    ItemCatalog.Find(quest.RewardItemKey)?.Name,
                    IsLocked: quest.MinimumLevel > level,
                    MinimumLevel: quest.MinimumLevel);
            })
            .OrderBy(q => q.ClaimedAt is not null)   // claimed sink to the bottom
            .ThenBy(q => q.IsLocked)                 // then the ones you cannot take
            .ThenByDescending(q => q.IsComplete)     // claimable first
            .ThenBy(q => q.MinimumLevel)
            .ToList();
    }

    public async Task<RpgResult<QuestClaim>> ClaimAsync(
        Guid userId,
        string questKey,
        CancellationToken cancellationToken)
    {
        var quest = QuestCatalog.Find(questKey);

        if (quest is null)
        {
            return RpgResult<QuestClaim>.Fail(RpgFailure.NotFound, $"No quest called '{questKey}'.");
        }

        var progress = await db.QuestProgress
            .FirstOrDefaultAsync(p => p.UserId == userId && p.QuestKey == questKey, cancellationToken);

        if (progress is null || !IsComplete(quest, ReadCounters(progress)))
        {
            return RpgResult<QuestClaim>.Fail(
                RpgFailure.QuestNotComplete, "That quest is not finished yet.");
        }

        if (progress.ClaimedAt is not null)
        {
            return RpgResult<QuestClaim>.Fail(
                RpgFailure.QuestAlreadyClaimed, "That reward has already been claimed.");
        }

        var character = await db.Characters.SingleAsync(c => c.UserId == userId, cancellationToken);

        progress.ClaimedAt = DateTimeOffset.UtcNow;
        character.Gold += quest.RewardGold;

        InventoryItem? item = null;

        if (quest.RewardItemKey is not null)
        {
            // Quest rewards are guaranteed Uncommon: a fixed reward that could roll Common
            // would feel like a punishment for finishing.
            item = loot.Grant(userId, quest.RewardItemKey, Rarity.Uncommon);
        }

        // Deliberately absent: any change to character.TotalXp.
        await db.SaveChangesAsync(cancellationToken);

        if (quest.RewardGold > 0)
        {
            await RecordAsync(userId, ObjectiveKind.EarnGold, string.Empty, quest.RewardGold, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return RpgResult<QuestClaim>.Success(new QuestClaim(quest.RewardGold, character.Gold, item));
    }

    private async Task<int> CharacterLevelAsync(Guid userId, CancellationToken cancellationToken)
    {
        var totalXp = await db.Characters
            .Where(c => c.UserId == userId)
            .Select(c => c.TotalXp)
            .FirstOrDefaultAsync(cancellationToken);

        return LevelCurve.LevelForXp(totalXp);
    }

    private static bool Matches(QuestObjective objective, ObjectiveKind kind, string target) =>
        objective.Kind == kind &&
        (objective.Target.Length == 0 ||
         string.Equals(objective.Target, target, StringComparison.OrdinalIgnoreCase));

    private static bool IsComplete(QuestDefinition quest, Dictionary<string, int> counters) =>
        quest.Objectives.All(o => counters.GetValueOrDefault(o.Id) >= o.Required);

    private static string Summarise(QuestDefinition quest, Dictionary<string, int> counters)
    {
        var done = quest.Objectives.Sum(o => Math.Min(o.Required, counters.GetValueOrDefault(o.Id)));
        var required = quest.Objectives.Sum(o => o.Required);

        return $"{done}/{required}";
    }

    private static Dictionary<string, int> ReadCounters(QuestProgress progress)
    {
        if (string.IsNullOrWhiteSpace(progress.Counters))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(progress.Counters) ?? [];
        }
        catch (JsonException)
        {
            // Unreadable progress resets rather than breaking the quest board entirely.
            return [];
        }
    }

    private static void WriteCounters(QuestProgress progress, Dictionary<string, int> counters) =>
        progress.Counters = JsonSerializer.Serialize(counters);
}
