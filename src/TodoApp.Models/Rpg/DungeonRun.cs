using System.Text.Json;

namespace TodoApp.Models.Rpg;

public enum DungeonRunStatus
{
    /// <summary>Zero on purpose: the partial unique index filters on Status = 0.</summary>
    Active = 0,
    Cleared = 1,
    Failed = 2,
    Abandoned = 3
}

/// <summary>
/// A dungeon in progress or finished. Persisted so a reload does not lose the run.
/// </summary>
/// <remarks>
/// Note what is not here: a depth counter. How deep a run has got is
/// <c>COUNT(encounters WHERE DungeonRunId = Id AND Status = Won)</c>, derived on read (DEC-002)
/// against an index built for exactly that count.
/// <para>
/// The bestiary's stored counters are the deliberate exception to that rule and had reasons this
/// row does not share: the chronicle stays prunable, and a sighting of something never killed has
/// nowhere else to live. Here the encounters are the source of truth, they are never pruned while
/// their run is live, and deriving takes a counter-advance write off the victory path entirely.
/// A stored depth could disagree with the rooms actually won; a derived one cannot.
/// </para>
/// </remarks>
public class DungeonRun
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public string DungeonKey { get; set; } = string.Empty;

    /// <summary>The rolled chain of monster keys, one per room, as a JSON array.</summary>
    /// <remarks>
    /// Stored because it is a rolled fact, which is exactly what DEC-002 says to store. Re-derived
    /// on read it would reshuffle on every request: a reload would be a free re-roll of a room
    /// the player did not like, and no two reads of the same run would agree.
    /// </remarks>
    public string Rooms { get; set; } = "[]";

    public DungeonRunStatus Status { get; set; } = DungeonRunStatus.Active;

    public int GoldAwarded { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? EndedAt { get; set; }

    public DungeonDefinition? Dungeon => DungeonCatalog.Find(DungeonKey);

    public bool IsOver => Status != DungeonRunStatus.Active;
}

/// <summary>
/// Reading and writing the rolled chain.
/// </summary>
/// <remarks>
/// Pure and static, the shape <see cref="StatusEffects"/> already set. Nothing here takes an
/// <c>IDiceRoller</c>, so the chain can only ever be rolled at the one site that opens a run and
/// can never be quietly re-rolled by a read.
/// </remarks>
public static class DungeonRuns
{
    /// <summary>The chain of monster keys a run rolled, in room order.</summary>
    /// <remarks>
    /// A corrupt blob reads as an empty chain rather than throwing, copying <c>StatusEffects.Read</c>
    /// and <c>ReadUses</c>. The trade is the same one and lands the same way: an empty chain makes
    /// the run unenterable and abandonable, where throwing would make it neither.
    /// </remarks>
    public static IReadOnlyList<string> Read(DungeonRun run)
    {
        if (string.IsNullOrWhiteSpace(run.Rooms))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(run.Rooms) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static void Write(DungeonRun run, IReadOnlyList<string> rooms) =>
        run.Rooms = JsonSerializer.Serialize(rooms);
}
