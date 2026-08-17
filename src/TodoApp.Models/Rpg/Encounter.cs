namespace TodoApp.Models.Rpg;

public enum EncounterStatus
{
    /// <summary>Zero on purpose: the partial unique index filters on Status = 0.</summary>
    Active = 0,
    Won = 1,
    Lost = 2,
    Fled = 3
}

/// <summary>
/// A fight in progress or finished. Persisted so a reload does not lose the encounter.
/// </summary>
public class Encounter
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public string MonsterKey { get; set; } = string.Empty;

    public int MonsterHitPoints { get; set; }

    public EncounterStatus Status { get; set; } = EncounterStatus.Active;

    public int Round { get; set; }

    /// <summary>
    /// The combat log as JSON.
    /// </summary>
    /// <remarks>
    /// Held as a serialised string in a jsonb column rather than a normalised rounds
    /// table: it is append-only, always read whole, and never queried by its contents, so
    /// normalising would buy nothing and cost a join on every request.
    /// </remarks>
    public string Log { get; set; } = "[]";

    public int GoldAwarded { get; set; }

    /// <summary>Whether the Cleric's Blessing reroll has been spent this encounter.</summary>
    public bool BlessingUsed { get; set; }

    /// <summary>
    /// Class ability uses spent this fight, as a JSON object keyed by ability key.
    /// </summary>
    /// <remarks>
    /// Per-encounter rather than a persistent resource, so there is nothing to track or
    /// replenish between fights. Held as JSON for the same reason as the log: it is read
    /// whole and never queried by its contents.
    /// </remarks>
    public string AbilityUses { get; set; } = "{}";

    /// <summary>
    /// Rounds the monster still attacks at disadvantage for, from the Bard's Vicious
    /// Mockery. Decremented as it is consumed.
    /// </summary>
    public int MonsterDisadvantageRounds { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? EndedAt { get; set; }

    public MonsterDefinition? Monster => MonsterCatalog.Find(MonsterKey);

    public bool IsOver => Status != EncounterStatus.Active;
}
