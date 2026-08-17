namespace TodoApp.Models;

public class TodoTask
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Owning user. Every read and write of this table is filtered by it.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Parent task when this is a subtask, null when it is a task in its own right.
    /// </summary>
    /// <remarks>
    /// A subtask is a row in this same table rather than a separate entity, so it inherits
    /// ownership, scoping, indexes and the whole endpoint surface for free. One level only:
    /// the parent of a subtask must itself have no parent, enforced on write.
    /// </remarks>
    public Guid? ParentId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public Difficulty Difficulty { get; set; } = Difficulty.Medium;

    public Priority Priority { get; set; } = Priority.Normal;

    /// <summary>Free-form labels. Postgres text[], so filtering stays a single query.</summary>
    public List<string> Tags { get; set; } = [];

    public DateTimeOffset? DueDate { get; set; }

    /// <summary>Todo, in progress, or completed.</summary>
    public TaskProgress Status { get; set; } = TaskProgress.Todo;

    /// <summary>
    /// Convenience over <see cref="Status"/>, not a stored column.
    /// </summary>
    /// <remarks>
    /// Deliberately not mapped. Storing both this and <see cref="Status"/> would be two
    /// copies of one fact, which is the drift DEC-002 exists to prevent. It cannot appear
    /// in a LINQ query that reaches the database; use <c>Status == TaskProgress.Completed</c>
    /// there, which is what the index is built on.
    /// </remarks>
    public bool IsCompleted => Status == TaskProgress.Completed;

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>When work actually started, for the in-progress state.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// XP actually granted when this task was completed. Snapshotted so that editing the
    /// difficulty afterwards can never retroactively change the character's total.
    /// </summary>
    public int XpAwarded { get; set; }

    /// <summary>
    /// Stamina actually granted when this task was completed, snapshotted for the same
    /// reason as <see cref="XpAwarded"/>.
    /// </summary>
    /// <remarks>
    /// Reopening used to refund XP and leave the stamina behind, so completing and
    /// reopening one Epic task in a loop minted five stamina and five hit points a cycle
    /// out of no work at all. Stamina is the gate that makes the whole RPG layer a sink
    /// for real work (DEC-012), so an unbounded source of it is an unbounded source of
    /// gold and loot.
    /// </remarks>
    public int StaminaAwarded { get; set; }

    public RecurrenceRule Recurrence { get; set; } = RecurrenceRule.None;

    /// <summary>
    /// The earliest completion that may pay XP again. Null means always eligible.
    /// </summary>
    /// <remarks>
    /// The anti-inflation gate for recurrence. It moves forward on a paying completion and
    /// is never cleared by editing, so "set daily, complete, set none, complete, set daily"
    /// is not a hundred XP for three clicks. It <i>is</i> cleared by reopening a completion
    /// that paid, because reopening hands the XP back: leaving the gate shut there would
    /// mean an accidental tick and untick destroyed the day's reward outright. The previous
    /// paying completion was by definition a whole period earlier, so a fresh payout is
    /// genuinely due.
    /// </remarks>
    public DateTimeOffset? XpEligibleFrom { get; set; }

    /// <summary>Manual ordering within the list; lower sorts first.</summary>
    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Whether finishing this task may move XP, stamina, badges or quest progress.
    /// </summary>
    /// <remarks>
    /// The single gate for every progression rule, deliberately in one place. The
    /// alternative was repeating "ParentId is null" across the completion path, three
    /// achievement counts, two quest recordings and four stats aggregates, where one
    /// missed filter leaks silently: a quest paying gold for twenty subtask completions is
    /// a DEC-012 breach through the side door, and no XP test would catch it.
    ///
    /// Subtasks never bear progression. Splitting one task into twenty would otherwise
    /// multiply its reward twentyfold for the same work.
    /// </remarks>
    public bool IsProgressionBearing => ParentId is null;

    /// <summary>
    /// Whether a completion at this moment may pay out, given recurrence.
    /// </summary>
    public bool MayAwardAt(DateTimeOffset moment) =>
        IsProgressionBearing && (XpEligibleFrom is null || moment >= XpEligibleFrom.Value);

    /// <summary>
    /// The status as of a given moment, which is what the user should see.
    /// </summary>
    /// <remarks>
    /// A recurring task comes back on its own. "Water the plants", ticked on Monday, is a
    /// thing you still have to do on Tuesday, so once its period has rolled over the stored
    /// Completed reads as Todo again. Derived rather than written by a nightly job, in
    /// keeping with DEC-002: the eligibility stamp already holds the fact, and a scheduled
    /// task that resets rows is a second copy of it that can be missed, run twice, or run
    /// while the user is mid-edit.
    /// </remarks>
    public TaskProgress StatusAt(DateTimeOffset moment) =>
        Status == TaskProgress.Completed
        && Recurrence != RecurrenceRule.None
        && XpEligibleFrom is not null
        && moment >= XpEligibleFrom.Value
            ? TaskProgress.Todo
            : Status;

    /// <summary>Completion as the user sees it, with recurrence rollover applied.</summary>
    public bool IsCompletedAt(DateTimeOffset moment) =>
        StatusAt(moment) == TaskProgress.Completed;

    /// <summary>Days past the due date, or zero. Drives the overdue bounty (DEC-013).</summary>
    public int DaysOverdue(DateTimeOffset now) =>
        DueDate is null || IsCompletedAt(now) || now <= DueDate.Value
            ? 0
            : (int)(now - DueDate.Value).TotalDays;
}
