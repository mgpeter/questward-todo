namespace TodoApp.Models.Rpg;

/// <summary>
/// What one user has met, and what it cost the monster. One row per user per monster,
/// written when a fight starts and again when it is won.
/// </summary>
/// <remarks>
/// The four counters are a deliberate DEC-002 exception, not an oversight: the chronicle is
/// prunable and a sighting is not a win, so a GROUP BY over "encounters" cannot stand in for
/// them. The full argument is in the AddBestiary migration, above its backfill.
/// Everything else a bestiary page shows is derived through <see cref="MonsterCatalog"/> per
/// DEC-004 and stays unmapped, since a get-only property with no backing field is not
/// discovered by EF Core.
/// </remarks>
public class BestiaryEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public string MonsterKey { get; set; } = string.Empty;

    /// <summary>Fights started against it, whatever the outcome. A sighting, not a win.</summary>
    public int Encounters { get; set; }

    public int Kills { get; set; }

    public int GoldTaken { get; set; }

    /// <summary>Fewest rounds to a kill. Zero means never killed, which is why it is not nullable.</summary>
    public int BestRound { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public MonsterDefinition? Definition => MonsterCatalog.Find(MonsterKey);

    public bool IsSlain => Kills > 0;
}
