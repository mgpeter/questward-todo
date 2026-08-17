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
    /// The next moment a recurring task becomes worth XP again.
    /// </summary>
    /// <remarks>
    /// This is the anti-inflation gate for recurrence. Without it, a daily task is an XP
    /// printer: complete, reopen, complete, forever. The rule is that a recurring task
    /// pays once per period, and the eligibility stamp only ever moves forward.
    /// </remarks>
    public static DateTimeOffset? NextEligibleAfter(RecurrenceRule rule, DateTimeOffset completedAt) =>
        rule switch
        {
            RecurrenceRule.Daily => completedAt.AddDays(1),
            RecurrenceRule.Weekly => completedAt.AddDays(7),
            RecurrenceRule.Monthly => completedAt.AddMonths(1),
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
