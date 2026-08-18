namespace TodoApp.Models.Rpg;

/// <summary>Where a contract is in its three steps.</summary>
/// <remarks>
/// The order is the lifecycle and the lifecycle is the rule: a contract is accepted for nothing,
/// discharged by the work being finished, and only then fought. There is no member for "taken and
/// fightable", because that state is what DEC-013 forbids: it would pay bounty gold, loot and
/// standing for a task that is still not done, and the longer it was neglected the better it
/// would pay.
/// </remarks>
public enum HuntContractStatus
{
    /// <summary>
    /// Taken, and the work behind it is still outstanding. Zero on purpose: the partial unique
    /// index that stops one task carrying two live contracts filters on the two open states.
    /// </summary>
    Accepted = 0,

    /// <summary>The work is done. The fight is unlocked, and it costs one stamina like any other.</summary>
    Discharged = 1,

    /// <summary>
    /// The fight was opened. Everything after this belongs to the encounter row, including how
    /// it ended: won, lost or fled.
    /// </summary>
    Fought = 2,

    /// <summary>Torn up, either by the hunter or by the task being deleted underneath it.</summary>
    Abandoned = 3
}

/// <summary>
/// A task written up as a contract: the promise, before there is a fight.
/// </summary>
/// <remarks>
/// The row that makes accepting free. A contract used to be an encounter opened on the spot, which
/// meant taking one cost a stamina and the monster could be killed while the task it was written
/// on stayed undone. DEC-013 says a backlog is a bounty and never a toll, so accepting writes this
/// and nothing else: no stamina, no encounter, no die.
/// <para>
/// The six scalars below are frozen here for the reason <see cref="Encounter"/> freezes its copies
/// of them: they are what the contract was written against, and a task is one keystroke from being
/// re-dated, retagged, re-graded or split. Freezing them at acceptance also means waiting after
/// accepting cannot raise the purse, so there is no reason to sit on an accepted contract.
/// </para>
/// <para>
/// Storing them is not a DEC-002 breach. What is stored is the historical fact "this is what was
/// written down", and every number derived from it (the stat block, the bounty percentage, the
/// reward floor) is recomputed on every read from <see cref="HuntRules"/> and the catalogs.
/// </para>
/// </remarks>
public class HuntContract
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    /// <summary>The task this contract was written on, or null once that task has been deleted.</summary>
    /// <remarks>
    /// ON DELETE SET NULL, matching the encounter's link and for the same reason: DeleteTask runs
    /// ExecuteDeleteAsync and bypasses the change tracker, so the referential action is all there
    /// is between "the user tidied a task away" and a contract that vanishes with it. An accepted
    /// contract is swept to <see cref="HuntContractStatus.Abandoned"/> before the delete, because
    /// there is no longer any work that could discharge it. A discharged one survives with a null
    /// link and stays fightable: the work was done, and tidying the row away afterwards must not
    /// take back what doing it earned.
    /// </remarks>
    public Guid? TaskId { get; set; }

    /// <summary>
    /// The task's own words, frozen. Used once, for the opening line of the fight it buys.
    /// </summary>
    /// <remarks>
    /// Copied rather than read back through <see cref="TaskId"/> so a contract whose task has been
    /// deleted still announces itself as what it was written on. It is the only user text a hunt
    /// ever carries; MonsterKey is a catalog key, so the combat log and the chronicle stay free of
    /// it.
    /// </remarks>
    public string TaskTitle { get; set; } = string.Empty;

    /// <summary>The shape the task took, as a <see cref="HuntArchetypeCatalog"/> key (DEC-004).</summary>
    public string ArchetypeKey { get; set; } = string.Empty;

    /// <summary>The rung this contract was written at, from the hunter's level at acceptance.</summary>
    public int Level { get; set; }

    /// <summary>How overdue the task was when the contract was accepted.</summary>
    public int DaysOverdue { get; set; }

    /// <summary>How many subtasks it carried when the contract was accepted.</summary>
    public int Subtasks { get; set; }

    /// <summary>The banner it flies, as a <see cref="FactionCatalog"/> key, or null for none.</summary>
    public string? FactionKey { get; set; }

    public HuntContractStatus Status { get; set; } = HuntContractStatus.Accepted;

    public DateTimeOffset AcceptedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the work was finished, or null while it is still outstanding.
    /// </summary>
    /// <remarks>
    /// Written only by a completion that postdates <see cref="AcceptedAt"/> and that the DEC-014
    /// gate actually paid for. That comparison is the whole of why this column exists: without it
    /// a completion from a previous recurrence period can be made to read as current by an
    /// ordinary edit, and a contract would settle in full on work done in another window.
    /// </remarks>
    public DateTimeOffset? DischargedAt { get; set; }

    /// <summary>The fight this contract bought, or null until one is opened.</summary>
    public Guid? EncounterId { get; set; }

    /// <summary>When the contract stopped being live, by being fought or torn up.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Whether this contract is still the one live contract on its task.</summary>
    public bool IsLive =>
        Status is HuntContractStatus.Accepted or HuntContractStatus.Discharged;

    /// <summary>Whether the fight may be opened. The single answer, asked everywhere.</summary>
    /// <remarks>
    /// Reads the recorded discharge and never the task, which is what makes it impossible to
    /// answer with a stale snapshot: <see cref="DischargedAt"/> is written by the one place that
    /// checks the completion against <see cref="AcceptedAt"/>, and no edit to the task can reach
    /// back and set it.
    /// </remarks>
    public bool MayBeFought => Status == HuntContractStatus.Discharged;

    /// <summary>The block this contract is worth, derived from the frozen facts on every read.</summary>
    public MonsterDefinition? Monster =>
        HuntArchetypeCatalog.Find(ArchetypeKey) is { } archetype
            ? HuntRules.StatBlock(archetype, Level, DaysOverdue, Subtasks)
            : null;
}
