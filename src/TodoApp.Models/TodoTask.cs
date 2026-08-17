namespace TodoApp.Models;

public class TodoTask
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Owning user. Every read and write of this table is filtered by it.</summary>
    public Guid UserId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public Difficulty Difficulty { get; set; } = Difficulty.Medium;

    public Priority Priority { get; set; } = Priority.Normal;

    public DateTimeOffset? DueDate { get; set; }

    public bool IsCompleted { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

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

    /// <summary>Manual ordering within the list; lower sorts first.</summary>
    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
