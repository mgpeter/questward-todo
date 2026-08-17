namespace TodoApp.Api.Services.Rpg;

public enum RpgFailure
{
    None = 0,
    NotFound = 1,
    EncounterAlreadyActive = 2,
    NotEnoughStamina = 3,
    MonsterOutOfRange = 4,
    EncounterOver = 5,
    ItemEquipped = 6,
    QuestNotComplete = 7,
    QuestAlreadyClaimed = 8,
    UnknownClass = 9
}

/// <summary>
/// A small result type so services can report a specific failure without throwing.
/// Endpoints map the failure to a status code in one place.
/// </summary>
public readonly record struct RpgResult<T>(T? Value, RpgFailure Failure, string? Message)
{
    public bool Ok => Failure == RpgFailure.None;

    public static RpgResult<T> Success(T value) => new(value, RpgFailure.None, null);

    public static RpgResult<T> Fail(RpgFailure failure, string message) =>
        new(default, failure, message);
}
