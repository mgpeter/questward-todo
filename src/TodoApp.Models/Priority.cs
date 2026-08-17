namespace TodoApp.Models;

/// <summary>
/// Ordering hint only. Deliberately does not affect XP, so that priority stays
/// an organisational tool rather than a way to farm levels.
/// </summary>
public enum Priority
{
    Low = 0,
    Normal = 1,
    High = 2
}
