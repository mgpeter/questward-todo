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
    /// Status effects riding this fight, as a serialised <see cref="StatusEffect"/> array.
    /// </summary>
    /// <remarks>
    /// On the encounter rather than the character, so nothing has to clean up after a fight
    /// and no affliction can leak into the next one. Held as JSON for the same reason as the
    /// log and the ability uses: read whole, never queried by its contents.
    /// <para>
    /// This replaced a bare MonsterDisadvantageRounds counter. One typed array covers that
    /// counter and everything shaped like it, so the next effect is a new case rather than a
    /// new column and another flag threaded through the attack path.
    /// </para>
    /// </remarks>
    public string Effects { get; set; } = "[]";

    /// <summary>The highest phase this fight has entered.</summary>
    /// <remarks>
    /// Deliberately allowed to disagree with <see cref="MonsterDefinition.PhaseAt"/> read
    /// against the current hit points. A monster that heals back above a threshold does not
    /// leave the phase it entered and does not enter it a second time. Without the high-water
    /// mark a regenerating boss re-triggers its entry every round it crosses back and re-applies
    /// its effects forever, which is an effect-inflation bug rather than a mechanic.
    /// <para>
    /// This is per-fight scratch state recording what happened, not a derived value that has
    /// drifted, so it is not a DEC-002 exception: the fight having entered a phase is a
    /// historical fact about this fight, and nothing else records it.
    /// </para>
    /// </remarks>
    public int Phase { get; set; }

    /// <summary>The dungeon run this fight is a room of, or null for a fight taken at the tavern.</summary>
    /// <remarks>
    /// The encounter points at the run and never the reverse, and that direction is the whole
    /// reason dungeons needed no change to the one-fight-at-a-time rule. A room's fight is an
    /// ordinary encounter row with Status = Active, so IX_encounters_UserId, filtered on
    /// "Status" = 0, governs it exactly as it governs a tavern fight. Had the run held the
    /// encounter instead, a dungeon fight would have been a second kind of fight and that index
    /// would have had to learn about it.
    /// </remarks>
    public Guid? DungeonRunId { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? EndedAt { get; set; }

    public MonsterDefinition? Monster => MonsterCatalog.Find(MonsterKey);

    public bool IsOver => Status != EncounterStatus.Active;
}
