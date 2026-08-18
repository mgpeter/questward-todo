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

        var discovered = await DiscoveredKindsAsync(userId, cancellationToken);

        // The caller writes the bestiary row before it calls in, so the kind being reported is
        // already in the derived set. It has to come back out for the "was this already
        // finished?" snapshot, or the discovery that completes a quest would report itself as
        // no advance at all and the player would never be told.
        var before = kind == ObjectiveKind.DiscoverMonster
            ? discovered
                .Where(k => !string.Equals(k, target, StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.Ordinal)
            : discovered;

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
            var wasComplete = IsComplete(quest, counters, before);
            var written = false;
            var advanced = false;

            foreach (var objective in quest.Objectives.Where(o => Matches(o, kind, target)))
            {
                if (IsDerived(objective))
                {
                    // Nothing to store: the bestiary row the caller has just added is the
                    // whole of this objective's progress. The advance is still reported, or a
                    // discovery would move the board without ever saying so.
                    advanced = true;
                    continue;
                }

                var current = counters.GetValueOrDefault(objective.Id);

                if (current >= objective.Required)
                {
                    continue;
                }

                // Clamped so an overshoot cannot make a later objective look further along
                // than it is.
                counters[objective.Id] = Math.Min(objective.Required, current + amount);
                written = true;
                advanced = true;
            }

            if (!advanced)
            {
                continue;
            }

            if (written)
            {
                WriteCounters(progress, counters);
            }

            var nowComplete = IsComplete(quest, counters, discovered);

            advances.Add(new QuestAdvance(
                quest.Key,
                quest.Name,
                Summarise(quest, counters, discovered),
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

        var discovered = await DiscoveredKindsAsync(userId, cancellationToken);

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
                            o.Id, o.Description, Current(o, counters, discovered), o.Required))
                        .ToList(),
                    IsComplete(quest, counters, discovered),
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

        if (progress?.ClaimedAt is not null)
        {
            return RpgResult<QuestClaim>.Fail(
                RpgFailure.QuestAlreadyClaimed, "That reward has already been claimed.");
        }

        var discovered = await DiscoveredKindsAsync(userId, cancellationToken);
        Dictionary<string, int> counters = progress is null ? [] : ReadCounters(progress);

        if (!IsComplete(quest, counters, discovered))
        {
            return RpgResult<QuestClaim>.Fail(
                RpgFailure.QuestNotComplete, "That quest is not finished yet.");
        }

        // Derived progress does not wait for the quest to unlock, so a low level character can
        // now satisfy a high level quest's objectives. The reward still waits for the level:
        // the board draws that quest as locked, and claiming has to mean what the board says.
        if (await CharacterLevelAsync(userId, cancellationToken) < quest.MinimumLevel)
        {
            return RpgResult<QuestClaim>.Fail(
                RpgFailure.QuestNotComplete,
                $"That reward is not open to you until level {quest.MinimumLevel}.");
        }

        if (progress is null)
        {
            // A quest whose objectives are all derived has no counter row until it is claimed,
            // because there was never anything to store. Claiming is what needs one.
            progress = new QuestProgress { UserId = userId, QuestKey = questKey };
            db.QuestProgress.Add(progress);
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

    /// <summary>Every kind of monster this user has ever met, read back from the chronicle.</summary>
    /// <remarks>
    /// Discovery progress is derived rather than counted up (DEC-002), and this is why. A
    /// stored counter only ever moved while the quest was already unlocked, and a kind is met
    /// for the first time exactly once, so every sighting made below a quest's MinimumLevel
    /// was dropped and could never be replayed. That put a hard ceiling on the high level
    /// discovery quests: the availability band offers a character at level 8 nothing below
    /// monster level 6, so a counter starting from zero on the day the quest unlocked could
    /// not reach the total the quest asked for and the quest sat on the board forever. The
    /// same arithmetic stranded anyone whose bestiary was seeded by the AddBestiary backfill,
    /// because a backfilled row turns every later sighting into a repeat. Reading the rows
    /// instead makes the count whatever actually happened, whenever it happened.
    /// </remarks>
    private async Task<HashSet<string>> DiscoveredKindsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var keys = await db.BestiaryEntries
            .Where(b => b.UserId == userId)
            .Select(b => b.MonsterKey)
            .ToListAsync(cancellationToken);

        var discovered = keys.ToHashSet(StringComparer.Ordinal);

        // A sighting added by the fight that is calling in has not been committed yet, so the
        // query above cannot see it. Without the overlay the discovery that opens a fight
        // would not count until some later fight happened to record something else.
        foreach (var pending in db.BestiaryEntries.Local.Where(b => b.UserId == userId))
        {
            discovered.Add(pending.MonsterKey);
        }

        return discovered;
    }

    private static bool Matches(QuestObjective objective, ObjectiveKind kind, string target) =>
        objective.Kind == kind &&
        (objective.Target.Length == 0 ||
         string.Equals(objective.Target, target, StringComparison.OrdinalIgnoreCase));

    /// <summary>Read from the bestiary rather than the counter map, so nothing is stored for it.</summary>
    private static bool IsDerived(QuestObjective objective) =>
        objective.Kind == ObjectiveKind.DiscoverMonster;

    private static int Current(
        QuestObjective objective,
        Dictionary<string, int> counters,
        HashSet<string> discovered)
    {
        if (!IsDerived(objective))
        {
            return counters.GetValueOrDefault(objective.Id);
        }

        // An empty target counts kinds; a named one asks whether that one kind has been met.
        return objective.Target.Length == 0
            ? discovered.Count
            : discovered.Contains(objective.Target) ? 1 : 0;
    }

    private static bool IsComplete(
        QuestDefinition quest,
        Dictionary<string, int> counters,
        HashSet<string> discovered) =>
        quest.Objectives.All(o => Current(o, counters, discovered) >= o.Required);

    private static string Summarise(
        QuestDefinition quest,
        Dictionary<string, int> counters,
        HashSet<string> discovered)
    {
        var done = quest.Objectives.Sum(o => Math.Min(o.Required, Current(o, counters, discovered)));
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
