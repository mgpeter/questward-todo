using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Models;
using TodoApp.Models.Rpg;

namespace TodoApp.Api.Services.Rpg;

/// <summary>
/// Writes and reads the journal.
/// </summary>
/// <remarks>
/// The chronicle used to be a read over <c>encounters</c>, which is why it could only ever show
/// fights. It is rows now, one per thing that happened, for the reason DEC-020 gives: an
/// ascension deletes the encounters, quests, contracts and runs a derived feed would have been
/// composed from, and the point of the journal is to outlive exactly that.
/// <para>
/// <see cref="Record"/> adds and does not save, following <c>QuestService.RecordAsync</c>. Every
/// entry therefore commits inside the transaction of the thing it records: a fight that rolls
/// back does not leave a line saying it happened.
/// </para>
/// </remarks>
public sealed class ChronicleService(TodoDbContext db)
{
    /// <summary>
    /// Writes one entry against a character already in hand. Does not save.
    /// </summary>
    /// <remarks>
    /// Takes the character rather than a user id so the era comes off the row the caller has
    /// already loaded and changed, which is both a query saved and the correct value: an
    /// ascension writes its own entry against the era it is leaving.
    /// </remarks>
    public ChronicleEntry Record(
        Character character,
        ChronicleKind kind,
        IReadOnlyDictionary<string, string> facts,
        Guid? encounterId = null,
        DateTimeOffset? at = null) =>
        Add(character.UserId, character.Ascensions, kind, facts, encounterId, at);

    /// <summary>
    /// Writes one entry for a caller with no character loaded, reading the era off the row.
    /// Does not save.
    /// </summary>
    public async Task<ChronicleEntry> RecordAsync(
        Guid userId,
        ChronicleKind kind,
        IReadOnlyDictionary<string, string> facts,
        CancellationToken cancellationToken,
        Guid? encounterId = null,
        DateTimeOffset? at = null)
    {
        var era = await db.Characters
            .Where(c => c.UserId == userId)
            .Select(c => c.Ascensions)
            .FirstOrDefaultAsync(cancellationToken);

        return Add(userId, era, kind, facts, encounterId, at);
    }

    /// <summary>Entries newest first, keyset paged on <c>OccurredAt</c>.</summary>
    /// <remarks>
    /// Keyset rather than offset, matching the fight history it replaces: the feed grows at the
    /// end a player is reading from, and an offset page would shift under them every time a
    /// fight finished.
    /// </remarks>
    public Task<List<ChronicleEntry>> HistoryAsync(
        Guid userId,
        int limit,
        DateTimeOffset? before,
        ChronicleKind? kind,
        CancellationToken cancellationToken)
    {
        var query = db.ChronicleEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId);

        if (before is not null)
        {
            query = query.Where(e => e.OccurredAt < before);
        }

        if (kind is not null)
        {
            query = query.Where(e => e.Kind == kind);
        }

        // Ordered to match IX_chronicle_entries_UserId_OccurredAt.
        return query
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);
    }

    /// <summary>The facts back out of the row, or an empty set if the JSON cannot be read.</summary>
    /// <remarks>
    /// Swallows the parse failure for the reason <c>CombatService.Deserialise</c> does: a row
    /// nobody can read is a line the narrator words vaguely, not a chronicle that fails to load.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> ReadFacts(ChronicleEntry entry)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(entry.Facts) ?? [];
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private ChronicleEntry Add(
        Guid userId,
        int era,
        ChronicleKind kind,
        IReadOnlyDictionary<string, string> facts,
        Guid? encounterId,
        DateTimeOffset? at)
    {
        var entry = new ChronicleEntry
        {
            UserId = userId,
            Kind = kind,
            Era = era,
            OccurredAt = at ?? DateTimeOffset.UtcNow,
            EncounterId = encounterId,

            // Empty values are dropped rather than written as blanks, so the narrator's "is this
            // fact present" test means what it says and a row does not carry gold: "0".
            Facts = JsonSerializer.Serialize(
                facts.Where(f => !string.IsNullOrWhiteSpace(f.Value))
                    .ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal))
        };

        db.ChronicleEntries.Add(entry);

        return entry;
    }
}
