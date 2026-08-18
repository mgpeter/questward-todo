using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Models.Progression;
using TodoApp.Models.Rpg;

namespace TodoApp.Api.Services.Rpg;

/// <param name="Entry">
/// Null until the monster has been met. The catalog row is always present, so the codex can
/// show what is still out there without inventing a zeroed row for it.
/// </param>
public sealed record BestiaryRow(MonsterDefinition Monster, BestiaryEntry? Entry);

public sealed record BestiaryCodex(IReadOnlyList<BestiaryRow> Rows, int Discovered, int Slain, int Total);

public sealed record LoreFragmentView(LoreFragment Fragment, bool IsUnlocked);

public sealed record LorePlaceView(LorePlace Place, IReadOnlyList<LoreFragmentView> Fragments);

public sealed record LoreCollection(IReadOnlyList<LorePlaceView> Places, int Unlocked, int Total);

/// <summary>
/// Writes the four stored bestiary counters and reads back the codex and the lore collection.
/// </summary>
/// <remarks>
/// The write methods deliberately do not save. The caller commits, so a sighting rides the
/// transaction that opened the fight and a kill rides the one that ended it, exactly as quest
/// progress already does. Nothing here touches <c>Character.TotalXp</c>: a chronicle records
/// what happened, it does not pay for it (DEC-012).
/// </remarks>
public sealed class BestiaryService(TodoDbContext db)
{
    /// <summary>
    /// Counts a fight begun, whatever its outcome.
    /// </summary>
    /// <returns>True when this is the first time the user has met this monster.</returns>
    public async Task<bool> RecordSightingAsync(
        Guid userId,
        string monsterKey,
        CancellationToken cancellationToken)
    {
        var entry = await FindAsync(userId, monsterKey, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (entry is null)
        {
            db.BestiaryEntries.Add(new BestiaryEntry
            {
                UserId = userId,
                MonsterKey = monsterKey,
                Encounters = 1,
                FirstSeenAt = now,
                LastSeenAt = now
            });

            return true;
        }

        entry.Encounters++;
        entry.LastSeenAt = now;

        return false;
    }

    /// <summary>Counts a fight won, with the gold it paid and the round it took.</summary>
    public async Task RecordKillAsync(
        Guid userId,
        string monsterKey,
        int round,
        int gold,
        CancellationToken cancellationToken)
    {
        var entry = await FindAsync(userId, monsterKey, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (entry is null)
        {
            // A fight that started before this table existed still ends in it, and the
            // backfill only saw encounters that had already been recorded. Without this the
            // first kill of such a fight would be lost rather than merely uncounted.
            entry = new BestiaryEntry
            {
                UserId = userId,
                MonsterKey = monsterKey,
                Encounters = 1,
                FirstSeenAt = now
            };

            db.BestiaryEntries.Add(entry);
        }

        entry.Kills++;
        entry.GoldTaken += gold;
        entry.LastSeenAt = now;

        // Zero is the never-killed sentinel, not a real round, so the first kill has to take
        // the round outright. Comparing on Math.Min alone would leave every entry on zero.
        if (entry.BestRound == 0 || round < entry.BestRound)
        {
            entry.BestRound = round;
        }
    }

    /// <summary>
    /// The whole catalog, met or not. A monster nobody has fought yet is still a reason to
    /// keep going, the same way the quest board shows quests that are still locked.
    /// </summary>
    public async Task<BestiaryCodex> CodexAsync(Guid userId, CancellationToken cancellationToken)
    {
        var entries = await db.BestiaryEntries
            .AsNoTracking()
            .Where(b => b.UserId == userId)
            .ToDictionaryAsync(b => b.MonsterKey, StringComparer.Ordinal, cancellationToken);

        var rows = MonsterCatalog.All
            .OrderBy(m => m.Level)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .Select(m => new BestiaryRow(m, entries.GetValueOrDefault(m.Key)))
            .ToList();

        return new BestiaryCodex(
            rows,
            Discovered: rows.Count(r => r.Entry is not null),
            Slain: rows.Count(r => r.Entry?.IsSlain == true),
            Total: rows.Count);
    }

    /// <summary>Every fragment, grouped by place, with the unlocked ones marked.</summary>
    public async Task<LoreCollection> LoreAsync(Guid userId, CancellationToken cancellationToken)
    {
        var state = await StateAsync(userId, cancellationToken);

        var places = LoreCatalog.Places
            .Select(place => new LorePlaceView(
                place,
                LoreCatalog.ForPlace(place.Key)
                    .Select(f => new LoreFragmentView(f, f.IsUnlockedBy(state)))
                    .ToList()))
            .ToList();

        return new LoreCollection(
            places,
            Unlocked: places.Sum(p => p.Fragments.Count(f => f.IsUnlocked)),
            Total: places.Sum(p => p.Fragments.Count));
    }

    /// <summary>
    /// Rebuilt per request from rows already stored. There is no lore_unlocks table because
    /// an unlock is a pure function of these three facts (DEC-002).
    /// </summary>
    private async Task<LoreState> StateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var totalXp = await db.Characters
            .Where(c => c.UserId == userId)
            .Select(c => c.TotalXp)
            .FirstOrDefaultAsync(cancellationToken);

        var bestiary = await db.BestiaryEntries
            .AsNoTracking()
            .Where(b => b.UserId == userId)
            .Select(b => new { b.MonsterKey, b.Encounters, b.Kills })
            .ToListAsync(cancellationToken);

        var claimed = await db.QuestProgress
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.ClaimedAt != null)
            .Select(p => p.QuestKey)
            .ToListAsync(cancellationToken);

        return new LoreState(
            LevelCurve.LevelForXp(totalXp),
            bestiary.ToDictionary(
                b => b.MonsterKey,
                b => (Seen: b.Encounters, Slain: b.Kills),
                StringComparer.Ordinal),
            claimed.ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>
    /// Local first, because a sighting added earlier in this same unit of work is not visible
    /// to a query. Missing it would add a second row and the unique index would reject the
    /// whole transaction, taking the fight with it.
    /// </summary>
    private async Task<BestiaryEntry?> FindAsync(
        Guid userId,
        string monsterKey,
        CancellationToken cancellationToken) =>
        db.BestiaryEntries.Local.FirstOrDefault(
            b => b.UserId == userId && string.Equals(b.MonsterKey, monsterKey, StringComparison.Ordinal))
        ?? await db.BestiaryEntries.FirstOrDefaultAsync(
            b => b.UserId == userId && b.MonsterKey == monsterKey, cancellationToken);
}
