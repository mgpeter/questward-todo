namespace TodoApp.Models.Rpg;

/// <summary>
/// What one chronicle entry records.
/// </summary>
/// <remarks>
/// Numbered and never renumbered: the value is stored, so a member that changes number rewrites
/// history rather than the code. New kinds append.
/// </remarks>
public enum ChronicleKind
{
    FightWon = 0,
    FightLost = 1,
    FightFled = 2,
    QuestClaimed = 3,
    ContractAccepted = 4,
    ContractSettled = 5,
    StandingRaised = 6,
    DungeonCleared = 7,
    DungeonFailed = 8,
    LevelReached = 9,
    Ascended = 10
}

/// <summary>
/// One line of the journal: something that happened, when it happened, and the facts it happened
/// to.
/// </summary>
/// <remarks>
/// Rows rather than a read-side union over encounters, quests, contracts and runs, which is what
/// the chronicle used to be. Ascending deletes all four of those tables, so a derived chronicle
/// would erase the history that makes ascending mean anything (DEC-020).
/// <para>
/// Nothing here is narrated. <see cref="Facts"/> holds catalog keys and numbers and
/// <see cref="ChronicleNarrator"/> turns them into a sentence on read, so rewording an entry is
/// a code change rather than a migration over every row that ever said it (DEC-004).
/// </para>
/// </remarks>
public class ChronicleEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public ChronicleKind Kind { get; set; }

    /// <summary>
    /// How many times the character had ascended when this was written.
    /// </summary>
    /// <remarks>
    /// Stored rather than derived from the ascension entries around it, because it is the one
    /// fact about an entry that its own row cannot recompute: the character's counter has moved
    /// on since. It is what the feed draws its era dividers from.
    /// </remarks>
    public int Era { get; set; }

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The fight this entry is about, while that fight still exists.
    /// </summary>
    /// <remarks>
    /// SET NULL on delete, for the reason the same rule exists on <c>Encounter.TaskId</c>: an
    /// ascension deletes every encounter, and the entry has to survive it. What is lost is the
    /// roll-by-roll log the row can expand into; what is kept is the sentence, because the
    /// narration reads <see cref="Facts"/> and never the encounter.
    /// </remarks>
    public Guid? EncounterId { get; set; }

    /// <summary>
    /// The entry's facts as a flat JSON object of strings: catalog keys, counts and amounts.
    /// </summary>
    /// <remarks>
    /// Flat and stringly typed on purpose. Every kind wants a different three or four fields, and
    /// a column per field would be a migration per kind and a table of mostly nulls. Unknown keys
    /// read as absent, so adding a fact to a kind leaves older rows narrating the shorter way.
    /// </remarks>
    public string Facts { get; set; } = "{}";
}
