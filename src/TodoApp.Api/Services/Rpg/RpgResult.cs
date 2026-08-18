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
    UnknownClass = 9,
    AbilityExhausted = 10,
    NotEnoughGold = 11,
    AlreadyAtFullHealth = 12,
    CannotUpgrade = 13,

    /// <summary>
    /// Its own member rather than a reuse of <see cref="NotEnoughGold"/>. Essence and gold are
    /// separate balances with separate sources, and a message telling someone to earn gold
    /// when the forge wants essence sends them to the wrong screen.
    /// </summary>
    NotEnoughEssence = 14,

    /// <summary>
    /// The offer was already bought today. A state conflict rather than a missing offer: the
    /// offer is on the shelf and a NotFound would tell the player to go looking for it.
    /// </summary>
    OfferSoldOut = 15,

    /// <summary>
    /// The item exists and belongs to the caller, but not for this. A potion cannot be worn and
    /// a sword cannot be drunk. A bad request rather than a state conflict: no amount of waiting
    /// changes the answer.
    /// </summary>
    ItemNotUsable = 16,

    /// <summary>
    /// The stack is empty. A state conflict rather than a NotFound: the row is real and the
    /// player is looking straight at it, and telling them it does not exist sends them hunting
    /// for something they can see.
    /// </summary>
    NoneLeft = 17,

    /// <summary>
    /// A dungeon run is already open. Its own member rather than a reuse of
    /// <see cref="EncounterAlreadyActive"/>: the two are refused from different screens and want
    /// different advice. "You are already in a fight" sent to someone standing outside a dungeon
    /// tells them to look for a fight that is not there.
    /// </summary>
    DungeonInProgress = 18,

    /// <summary>
    /// No such run, or not this caller's. A NotFound rather than a state conflict, and
    /// deliberately the same answer for both, so run ids cannot be probed for existence.
    /// </summary>
    NoDungeonRun = 19,

    /// <summary>
    /// The run is finished. A state conflict rather than a NotFound: the run is real, it is on
    /// the screen, and the player can read how it ended.
    /// </summary>
    DungeonOver = 20
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
