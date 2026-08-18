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

    /// <summary>The task this fight is a contract on, or null for a fight taken at the tavern.</summary>
    /// <remarks>
    /// The same direction as <see cref="DungeonRunId"/> and for the same reason: a hunt is an
    /// ordinary encounter row, so IX_encounters_UserId governs it without being told about hunts.
    /// <para>
    /// The foreign key is ON DELETE SET NULL rather than cascade, which is the one place the two
    /// links differ. DeleteTask runs ExecuteDeleteAsync and bypasses the change tracker, so the
    /// referential action is the only thing between "the user tidied a task away" and "a fought
    /// battle, its gold and its log left the chronicle". Setting null loses the attribution and
    /// keeps the fight; the four frozen scalars below are untouched by it, so the stat block
    /// still derives and a live fight stays finishable rather than becoming unwinnable.
    /// </para>
    /// </remarks>
    public Guid? TaskId { get; set; }

    /// <summary>The rung this contract was written at, or null when this is not a hunt.</summary>
    /// <remarks>
    /// This is the discriminator: <see cref="IsHunt"/> reads it and nothing else, so one column
    /// answers "is this a hunt" without a second flag that could disagree with it.
    /// <para>
    /// Frozen at the start rather than re-derived, because it is computed from the hunter's level
    /// and a level rises mid-fight. Re-derived on read, a hunt would grow a rung the moment its
    /// owner levelled and MaxHitPoints would move underneath a fight in progress, which re-fires
    /// or skips phases and desyncs the client's health bar. DEC-002 says to store the rolled or
    /// historical fact, and which rung this contract was written at is exactly that.
    /// </para>
    /// </remarks>
    public int? HuntLevel { get; set; }

    /// <summary>How overdue the task was when the contract was taken.</summary>
    /// <remarks>
    /// Frozen for the same reason as the level, and this one is the sharper case: days overdue
    /// increases on its own with no user action at all, so a re-derived bounty would rise while
    /// the player was mid-fight and the gold range would widen between two reads of one
    /// encounter. It also has to survive the task being completed or deleted, at which point
    /// there is nothing left to derive it from.
    /// </remarks>
    public int? HuntDaysOverdue { get; set; }

    /// <summary>How many subtasks the task carried when the contract was taken.</summary>
    /// <remarks>
    /// Frozen with the rest. A subtask ticked off mid-fight would otherwise shrink the monster
    /// the player is currently hitting, which is a free heal in reverse and the same
    /// MaxHitPoints drift the level guards against.
    /// </remarks>
    public int? HuntSubtasks { get; set; }

    /// <summary>The banner this contract flies under, as a catalog key, or null for none.</summary>
    /// <remarks>
    /// The key only, per DEC-004; the faction's name, titles and reward table are read back from
    /// FactionCatalog on every request. Frozen rather than re-read from the task's tags because
    /// the tags are one keystroke from being changed: a faction derived on the read path would
    /// let a player retag a task immediately before the killing blow and redirect the reward to
    /// whichever banner is holding the item they want.
    /// </remarks>
    public string? HuntFactionKey { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>
    /// The stat block this fight is against, derived for a hunt and read from the bestiary
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// One property rather than two, because every reader downstream (ResolveRoundAsync,
    /// EncounterDto, the chronicle, the bestiary) goes through this one and a second accessor
    /// would be a second thing to remember to check.
    /// <para>
    /// Both branches resolve a key against a code-held catalog, so a hunt row still reads
    /// correctly long after its task is gone: the archetype is in HuntArchetypeCatalog and the
    /// four frozen scalars are on the row. HuntRules.StatBlock spends no die, which is what keeps
    /// a read off the blast radius of every seeded dice script in the suite.
    /// </para>
    /// </remarks>
    public MonsterDefinition? Monster =>
        HuntArchetypeCatalog.Find(MonsterKey) is { } archetype && HuntLevel is { } level
            ? HuntRules.StatBlock(archetype, level, HuntDaysOverdue ?? 0, HuntSubtasks ?? 0)
            : MonsterCatalog.Find(MonsterKey);

    /// <summary>Whether this fight is a contract on a task.</summary>
    /// <remarks>
    /// Reads <see cref="HuntLevel"/> rather than <see cref="TaskId"/> on purpose. The task link
    /// is nulled when the task is deleted, and a finished hunt whose task has been tidied away is
    /// still a hunt: it still derives its block from the archetype, still shows the name it was
    /// fought under, and still counts toward the faction it was taken for.
    /// </remarks>
    public bool IsHunt => HuntLevel is not null;

    public bool IsOver => Status != EncounterStatus.Active;
}
