using System.ComponentModel.DataAnnotations;
using TodoApp.Models;

namespace TodoApp.Api.Contracts;

public sealed record TaskDto(
    Guid Id,
    Guid? ParentId,
    string Title,
    string? Notes,
    Difficulty Difficulty,
    Priority Priority,
    IReadOnlyList<string> Tags,
    int XpValue,
    DateTimeOffset? DueDate,
    TaskProgress Status,
    bool IsCompleted,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? StartedAt,
    int XpAwarded,
    int StaminaAwarded,
    RecurrenceRule Recurrence,
    /// <summary>False when finishing this pays nothing: a subtask, or a repeat inside its period.</summary>
    bool AwardsProgression,
    int DaysOverdue,
    int SortOrder,
    IReadOnlyList<TaskDto> Subtasks,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateTaskRequest(
    [property: Required(AllowEmptyStrings = false, ErrorMessage = "A title is required.")]
    [property: StringLength(200, MinimumLength = 1)]
    string Title,
    [property: StringLength(4000)] string? Notes,
    Difficulty Difficulty = Difficulty.Medium,
    Priority Priority = Priority.Normal,
    DateTimeOffset? DueDate = null,
    IReadOnlyList<string>? Tags = null,
    RecurrenceRule Recurrence = RecurrenceRule.None,
    /// <summary>Set to nest this under an existing task. One level only.</summary>
    Guid? ParentId = null);

public sealed record UpdateTaskRequest(
    [property: Required(AllowEmptyStrings = false, ErrorMessage = "A title is required.")]
    [property: StringLength(200, MinimumLength = 1)]
    string Title,
    [property: StringLength(4000)] string? Notes,
    Difficulty Difficulty,
    Priority Priority,
    DateTimeOffset? DueDate,
    IReadOnlyList<string>? Tags = null,
    RecurrenceRule Recurrence = RecurrenceRule.None);

/// <summary>Moves a task between todo, in progress and completed.</summary>
public sealed record SetStatusRequest(
    TaskProgress Status,
    [property: Range(-840, 840)] int UtcOffsetMinutes = 0);

/// <summary>
/// One shape for all six transitions. Completing and reopening move XP while the other
/// transitions do not, but the client should not have to branch on the response type to
/// find out - it reads <see cref="XpDelta"/>, which is simply zero when nothing moved.
/// </summary>
public sealed record SetStatusResponse(
    TaskDto Task,
    int XpDelta,
    CharacterDto Character,
    bool LeveledUp,
    bool LeveledDown,
    int PreviousLevel,
    IReadOnlyList<AchievementDto> UnlockedAchievements);

/// <param name="UtcOffsetMinutes">
/// The client's UTC offset, so time-of-day achievements (Night Owl, Early Bird) and the
/// "tasks completed today" count are evaluated in the user's local day rather than the
/// server's, which is UTC inside the container.
/// </param>
public sealed record CompleteTaskRequest(
    [property: Range(-840, 840)] int UtcOffsetMinutes = 0);

public sealed record ReorderRequest(
    [property: Required][property: MinLength(1)] IReadOnlyList<Guid> OrderedIds);

public sealed record CompleteTaskResponse(
    TaskDto Task,
    int XpGained,
    CharacterDto Character,
    bool LeveledUp,
    int PreviousLevel,
    IReadOnlyList<AchievementDto> UnlockedAchievements);

public sealed record ReopenTaskResponse(
    TaskDto Task,
    int XpLost,
    CharacterDto Character,
    bool LeveledDown,
    int PreviousLevel);
