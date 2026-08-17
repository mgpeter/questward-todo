namespace TodoApp.Models;

public class TodoTask
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

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

    /// <summary>Manual ordering within the list; lower sorts first.</summary>
    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
