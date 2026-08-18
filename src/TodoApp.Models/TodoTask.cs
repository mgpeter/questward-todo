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
    /// The task this one spawned when it was completed, if it repeats.
    /// </summary>
    /// <remarks>
    /// Kept so reopening can take the successor back. Without it, complete-and-reopen leaves
    /// a trail of rows nobody asked for.
    /// <para>
    /// Deliberately a plain nullable id and not one half of a unique index across the series.
    /// An earlier design enforced "one live row per series" that way, and it broke the
    /// ordinary path: complete A, start its successor B, then reopen A, and the series has two
    /// live rows, the index rejects the write and reopening returns 500. Nothing here forbids
    /// two live rows, because a started successor is a real thing somebody is doing.
    /// </para>
    /// </remarks>
    public Guid? SpawnedTaskId { get; set; }

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


    /// <summary>Days past the due date, or zero. Drives the overdue bounty (DEC-013).</summary>
    /// <remarks>
    /// Honest for a repeating task now that completing one spawns a successor carrying the
    /// next due date. It used to lie: the due date never moved, so a daily task kept
    /// faithfully every day for a year reported itself a year overdue forever, and
    /// HuntService carried a second overdue calculation purely to work around it.
    /// </remarks>
    public int DaysOverdue(DateTimeOffset now) =>
        DueDate is null || IsCompleted || now <= DueDate.Value
            ? 0
            : (int)(now - DueDate.Value).TotalDays;
}
