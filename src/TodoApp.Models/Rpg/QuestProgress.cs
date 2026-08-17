namespace TodoApp.Models.Rpg;

/// <summary>
/// Per-user progress against one quest.
/// </summary>
public class QuestProgress
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public string QuestKey { get; set; } = string.Empty;

    /// <summary>
    /// Objective counters as a JSON object keyed by objective id.
    /// </summary>
    /// <remarks>
    /// Keyed by id rather than positional, so adding an objective to a quest does not
    /// invalidate existing rows. Unknown keys read as zero.
    /// </remarks>
    public string Counters { get; set; } = "{}";

    /// <summary>
    /// Null means unclaimed. "Claimable" stays a computation over the counters rather
    /// than a stored flag that could disagree with them.
    /// </summary>
    public DateTimeOffset? ClaimedAt { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public QuestDefinition? Definition => QuestCatalog.Find(QuestKey);
}
