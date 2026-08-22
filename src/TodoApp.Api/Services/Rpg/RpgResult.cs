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
    DungeonOver = 20,

    /// <summary>
    /// The task cannot carry a contract. A bad request rather than a state conflict, because no
    /// amount of waiting changes the answer for the two cases that produce it: a subtask never
    /// bears progression at all, and a task that is already done has nothing left to hunt.
    /// </summary>
    /// <remarks>
    /// Its own member rather than a reuse of <see cref="NotFound"/>. The task exists, the player
    /// is looking straight at it on their own list, and a 404 would send them hunting for a row
    /// that is on the screen.
    /// </remarks>
    NotHuntable = 21,

    /// <summary>
    /// The contract has been accepted but its task is not finished, so the fight is not open yet.
    /// A state conflict rather than a bad request: doing the work changes the answer, which is the
    /// whole point of the route.
    /// </summary>
    /// <remarks>
    /// Its own member rather than a reuse of <see cref="EncounterOver"/>, which says the opposite
    /// thing (the fight has already ended). Advice differs too: this one says finish the task, and
    /// that one says start a new fight. There is deliberately no "or fight it anyway" branch left
    /// to advise: paying a bounty on an unfinished task is what DEC-013 forbids.
    /// </remarks>
    HuntNotDischarged = 22,

    /// <summary>
    /// This task's contract for this window has already been taken. A state conflict rather than
    /// a bad request: for a recurring task the next period makes it huntable again, so waiting
    /// genuinely does change the answer.
    /// </summary>
    /// <remarks>
    /// Its own member rather than a reuse of <see cref="EncounterAlreadyActive"/>, which is about
    /// the one-fight-at-a-time rule and is refused from a different screen. There need be no
    /// fight open at all to hit this one: the contract may have been won an hour ago, or fled.
    /// </remarks>
    HuntAlreadyTaken = 23,

    /// <summary>
    /// The contract has already been collected on, or torn up. A state conflict rather than a
    /// NotFound: the row is real and the player can read how it ended.
    /// </summary>
    /// <remarks>
    /// Its own member rather than a reuse of <see cref="EncounterOver"/>, which is about a fight
    /// that has finished. A contract reaches this without any fight ever having been opened, by
    /// being abandoned, and telling that player their fight is over sends them looking for one.
    /// </remarks>
    HuntAlreadyFought = 24,

    /// <summary>
    /// Today's reroll ladder is spent. A state rather than a bad request, so 409.
    /// </summary>
    RerollsSpent = 25,

    /// <summary>
    /// The character has not reached <see cref="Models.Rpg.AscendRules.MinimumLevel"/>.
    /// </summary>
    /// <remarks>
    /// Its own member rather than a reuse of anything above it. Every other refusal in this list
    /// is about a thing the player asked for being unavailable; this one is about the player, and
    /// the only useful answer names the level they are climbing to. A bad request rather than a
    /// state conflict would be wrong too: waiting is exactly what changes the answer.
    /// </remarks>
    NotReadyToAscend = 26
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
