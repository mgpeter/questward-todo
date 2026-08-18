using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Models.Dice;
using TodoApp.Models.Rpg;

namespace TodoApp.Api.Services.Rpg;

/// <summary>
/// A run as every route reports it: the row, the chain it rolled, how deep it has got and the
/// fight that is open, if one is.
/// </summary>
/// <remarks>
/// <paramref name="Depth"/> is carried here rather than on <see cref="DungeonRun"/> because it is
/// derived (DEC-002): a count of the run's won encounters, read through
/// IX_encounters_DungeonRunId_Status. Everything a reload needs is on the row and in this count,
/// which is what makes the client stateless about a run.
/// </remarks>
public sealed record DungeonRunView(
    DungeonRun Run,
    IReadOnlyList<string> Rooms,
    int Depth,
    Encounter? Encounter);

/// <summary>
/// Opens dungeon runs, advances them a room at a time and closes them.
/// </summary>
/// <remarks>
/// Deliberately does not open fights itself. Every room goes through
/// <see cref="CombatService.StartAsync(Guid, string, DungeonRun, CancellationToken)"/>, which is
/// the same method the tavern calls and therefore the same stamina charge (DEC-012). A five room
/// run costs five stamina because it is five fights, and there is no code path here that could
/// price it any other way.
/// </remarks>
public sealed class DungeonService(
    TodoDbContext db,
    IDiceRoller roller,
    CharacterSheetService sheets,
    CombatService combat)
{
    /// <summary>Which dungeons the character has unlocked.</summary>
    public async Task<IReadOnlyList<DungeonDefinition>> AvailableAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var character = await db.Characters.SingleAsync(c => c.UserId == userId, cancellationToken);
        var sheet = await sheets.BuildAsync(character, cancellationToken);

        return DungeonCatalog.AvailableAt(sheet.Level);
    }

    /// <summary>
    /// Opens a run and rolls its chain of rooms.
    /// </summary>
    /// <remarks>
    /// The chain is rolled here, once, and written to the row, because it is a rolled fact and
    /// DEC-002 says to store those. Derived on read instead it would reshuffle on every request:
    /// a reload would be a free re-roll of a room the player did not like, and two reads of the
    /// same run would not agree with each other.
    /// <para>
    /// Costs one die per non-boss room and nothing else. The boss is fixed, so the last room is
    /// never rolled.
    /// </para>
    /// </remarks>
    public async Task<RpgResult<DungeonRunView>> StartAsync(
        Guid userId,
        string dungeonKey,
        CancellationToken cancellationToken)
    {
        var dungeon = DungeonCatalog.Find(dungeonKey);

        if (dungeon is null)
        {
            return RpgResult<DungeonRunView>.Fail(
                RpgFailure.NotFound, $"No dungeon called '{dungeonKey}'.");
        }

        var character = await db.Characters.SingleAsync(c => c.UserId == userId, cancellationToken);
        var sheet = await sheets.BuildAsync(character, cancellationToken);

        if (!dungeon.IsAvailableAt(sheet.Level))
        {
            // The same failure the tavern's level band returns, because it is the same fact: this
            // is out of your league. A separate member would map to the same 400 and say the same
            // thing in two vocabularies.
            return RpgResult<DungeonRunView>.Fail(
                RpgFailure.MonsterOutOfRange,
                $"{dungeon.Name} opens at level {dungeon.Level}. You are level {sheet.Level}.");
        }

        // Refused before anything is rolled, so a refusal cannot shift the dice a retry would see.
        if (await db.Encounters.AnyAsync(
                e => e.UserId == userId && e.Status == EncounterStatus.Active, cancellationToken))
        {
            return RpgResult<DungeonRunView>.Fail(
                RpgFailure.EncounterAlreadyActive,
                "Finish the fight you are in before going anywhere.");
        }

        if (await HasActiveRunAsync(userId, cancellationToken))
        {
            return RpgResult<DungeonRunView>.Fail(
                RpgFailure.DungeonInProgress, "You are already in a dungeon.");
        }

        var run = new DungeonRun
        {
            UserId = userId,
            DungeonKey = dungeon.Key,
            Status = DungeonRunStatus.Active
        };

        var rooms = RollChain(dungeon);

        DungeonRuns.Write(run, rooms);
        db.DungeonRuns.Add(run);

        await db.SaveChangesAsync(cancellationToken);

        return RpgResult<DungeonRunView>.Success(new DungeonRunView(run, rooms, Depth: 0, Encounter: null));
    }

    /// <summary>The run in progress, or null. Rolls nothing.</summary>
    public async Task<DungeonRunView?> ActiveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var run = await db.DungeonRuns.FirstOrDefaultAsync(
            r => r.UserId == userId && r.Status == DungeonRunStatus.Active, cancellationToken);

        return run is null ? null : await ViewAsync(run, cancellationToken);
    }

    /// <summary>
    /// Opens the next room's fight.
    /// </summary>
    /// <remarks>
    /// This is where a room is paid for. The stamina comes off inside
    /// <see cref="CombatService.StartAsync(Guid, string, DungeonRun, CancellationToken)"/>, on the
    /// same line as a tavern fight's, and the failure it returns when there is none left is
    /// forwarded unchanged. Nothing here can make a room cheaper than a fight.
    /// </remarks>
    public async Task<RpgResult<DungeonRunView>> EnterAsync(
        Guid userId,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var found = await FindLiveRunAsync(userId, runId, cancellationToken);

        if (!found.Ok)
        {
            return found;
        }

        var run = found.Value!.Run;

        // The friendly path. The partial unique index on encounters is what actually stops two
        // rooms opening at once; this is what turns that race into a 409 with a sentence rather
        // than a constraint violation and a 500.
        if (await db.Encounters.AnyAsync(
                e => e.UserId == userId && e.Status == EncounterStatus.Active, cancellationToken))
        {
            return RpgResult<DungeonRunView>.Fail(
                RpgFailure.EncounterAlreadyActive, "You are already in a fight.");
        }

        var rooms = found.Value.Rooms;
        var depth = found.Value.Depth;

        if (depth >= rooms.Count)
        {
            // Only reachable from a chain that failed to deserialise or a hand-edited row: the
            // room that takes the depth to the end of the chain closes the run as it is won.
            return RpgResult<DungeonRunView>.Fail(
                RpgFailure.DungeonOver, "There is nothing further in.");
        }

        var started = await combat.StartAsync(userId, rooms[depth], run, cancellationToken);

        if (!started.Ok)
        {
            return RpgResult<DungeonRunView>.Fail(started.Failure, started.Message ?? string.Empty);
        }

        return RpgResult<DungeonRunView>.Success(
            new DungeonRunView(run, rooms, depth, started.Value));
    }

    /// <summary>
    /// Walks out. Rolls nothing.
    /// </summary>
    /// <remarks>
    /// An open room is fled through the ordinary flee path rather than closed here, so the fight
    /// ends the one way fights end and the encounter slot is released by the code that already
    /// knows how. That call is also what marks the run Abandoned, which is why this method does
    /// not do it twice.
    /// </remarks>
    public async Task<RpgResult<DungeonRunView>> AbandonAsync(
        Guid userId,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var found = await FindLiveRunAsync(userId, runId, cancellationToken);

        if (!found.Ok)
        {
            return found;
        }

        var run = found.Value!.Run;

        var open = await db.Encounters.FirstOrDefaultAsync(
            e => e.DungeonRunId == run.Id && e.Status == EncounterStatus.Active, cancellationToken);

        if (open is not null)
        {
            var fled = await combat.FleeAsync(userId, open.Id, cancellationToken);

            if (!fled.Ok)
            {
                return RpgResult<DungeonRunView>.Fail(fled.Failure, fled.Message ?? string.Empty);
            }
        }
        else
        {
            run.Status = DungeonRunStatus.Abandoned;
            run.EndedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(cancellationToken);
        }

        return RpgResult<DungeonRunView>.Success(await ViewAsync(run, cancellationToken));
    }

    private Task<bool> HasActiveRunAsync(Guid userId, CancellationToken cancellationToken) =>
        db.DungeonRuns.AnyAsync(
            r => r.UserId == userId && r.Status == DungeonRunStatus.Active, cancellationToken);

    /// <summary>
    /// The run behind an id, refused when it is missing or finished.
    /// </summary>
    /// <remarks>
    /// Another user's run answers exactly as one that never existed does, so run ids cannot be
    /// probed for existence.
    /// </remarks>
    private async Task<RpgResult<DungeonRunView>> FindLiveRunAsync(
        Guid userId,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await db.DungeonRuns.FirstOrDefaultAsync(
            r => r.Id == runId && r.UserId == userId, cancellationToken);

        if (run is null)
        {
            return RpgResult<DungeonRunView>.Fail(RpgFailure.NoDungeonRun, "No such dungeon run.");
        }

        if (run.IsOver)
        {
            return RpgResult<DungeonRunView>.Fail(
                RpgFailure.DungeonOver, "That run is already over.");
        }

        return RpgResult<DungeonRunView>.Success(await ViewAsync(run, cancellationToken));
    }

    /// <summary>Reads the two derived things a run is reported with: its depth and its open fight.</summary>
    private async Task<DungeonRunView> ViewAsync(DungeonRun run, CancellationToken cancellationToken)
    {
        var depth = await DepthAsync(run.Id, cancellationToken);

        var encounter = await db.Encounters.FirstOrDefaultAsync(
            e => e.DungeonRunId == run.Id && e.Status == EncounterStatus.Active, cancellationToken);

        return new DungeonRunView(run, DungeonRuns.Read(run), depth, encounter);
    }

    /// <summary>
    /// How many rooms are behind the player: a count, never a stored counter (DEC-002).
    /// </summary>
    /// <remarks>
    /// The win is committed in the same transaction as the fight that produced it, so there is no
    /// window in which a room is cleared and not yet counted. It is also the index of the next
    /// room, which is what makes resuming after a reload a single read.
    /// </remarks>
    private Task<int> DepthAsync(Guid runId, CancellationToken cancellationToken) =>
        db.Encounters.CountAsync(
            e => e.DungeonRunId == runId && e.Status == EncounterStatus.Won, cancellationToken);

    /// <summary>
    /// Rolls the chain: one weighted draw per room, and the boss in the last one.
    /// </summary>
    /// <remarks>
    /// Exactly <c>Rooms - 1</c> dice. The boss is fixed by the catalog and costs nothing, which is
    /// also what guarantees the last room is the boss no matter how the pool is retuned.
    /// </remarks>
    private IReadOnlyList<string> RollChain(DungeonDefinition dungeon)
    {
        List<string> rooms = [];

        for (var i = 0; i < dungeon.Rooms - 1; i++)
        {
            rooms.Add(LootService.PickWeighted(dungeon.Pool, r => r.Weight, roller).MonsterKey);
        }

        rooms.Add(dungeon.BossKey);

        return rooms;
    }
}
