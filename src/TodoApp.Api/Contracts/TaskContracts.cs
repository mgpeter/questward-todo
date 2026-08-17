using System.ComponentModel.DataAnnotations;
using TodoApp.Models;

namespace TodoApp.Api.Contracts;

public sealed record TaskDto(
    Guid Id,
    string Title,
    string? Notes,
    Difficulty Difficulty,
    Priority Priority,
    int XpValue,
    DateTimeOffset? DueDate,
    bool IsCompleted,
    DateTimeOffset? CompletedAt,
    int XpAwarded,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateTaskRequest(
    [property: Required(AllowEmptyStrings = false, ErrorMessage = "A title is required.")]
    [property: StringLength(200, MinimumLength = 1)]
    string Title,
    [property: StringLength(4000)] string? Notes,
    Difficulty Difficulty = Difficulty.Medium,
    Priority Priority = Priority.Normal,
    DateTimeOffset? DueDate = null);

public sealed record UpdateTaskRequest(
    [property: Required(AllowEmptyStrings = false, ErrorMessage = "A title is required.")]
    [property: StringLength(200, MinimumLength = 1)]
    string Title,
    [property: StringLength(4000)] string? Notes,
    Difficulty Difficulty,
    Priority Priority,
    DateTimeOffset? DueDate);

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
