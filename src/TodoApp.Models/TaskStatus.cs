namespace TodoApp.Models;

/// <summary>
/// Where a task is in its life. Replaces the old <c>IsCompleted</c> boolean.
/// </summary>
/// <remarks>
/// Deliberately three states and not more. "In progress" is the one that earns its place:
/// it is the difference between a list of things you have not started and a list of things
/// you are actually holding.
/// </remarks>
public enum TaskProgress
{
    Todo = 0,
    InProgress = 1,
    Completed = 2
}

/// <summary>How often a task comes back once finished.</summary>
public enum RecurrenceRule
{
    None = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3
}

public static class RecurrenceRules
{
    /// <summary>
    /// The due date the next occurrence carries, one cadence on from <paramref name="from"/>.
    /// </summary>
    /// <remarks>
    /// Anchored on the previous DUE date where there is one, so a weekly task due every Monday
    /// stays due on Mondays however late it is actually ticked. Only a task with no due date at
    /// all anchors on the completion, because there is nothing else to anchor to. The caller
    /// picks which; this only knows the cadence.
    /// <para>
    /// This replaced an "earliest completion that may pay again" gate. Recurrence used to hold
    /// one row that silently reappeared, and the gate stopped a daily paying twice in a day.
    /// It went with the model: completing a repeat now spawns a real successor, so the thing
    /// a repeat produces is another task rather than permission to be ticked again. See
    /// DEC-015.
    /// </para>
    /// </remarks>
    public static DateTimeOffset? Advance(RecurrenceRule rule, DateTimeOffset from) =>
        rule switch
        {
            RecurrenceRule.Daily => from.AddDays(1),
            RecurrenceRule.Weekly => from.AddDays(7),
            RecurrenceRule.Monthly => from.AddMonths(1),
            _ => null
        };

    public static string Describe(RecurrenceRule rule) => rule switch
    {
        RecurrenceRule.Daily => "Daily",
        RecurrenceRule.Weekly => "Weekly",
        RecurrenceRule.Monthly => "Monthly",
        _ => "Once"
    };
}
