using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Models;
using TodoApp.Models.Rpg;

namespace TodoApp.Api.Services.Rpg;

/// <summary>One line of the contract board: a task, priced, before anybody has taken it.</summary>
/// <remarks>
/// Derived on every read and stored nowhere. Nothing here rolls, so opening the board twice
/// quotes the same purse twice, and reading it costs no stamina and no die.
/// </remarks>
public sealed record HuntOffer(
    TodoTask Task,
    string ArchetypeKey,
    int Level,
    int DaysOverdue,
    int Subtasks,
    MonsterDefinition Monster,
    FactionDefinition? Faction,
    FactionStanding Standing);

/// <summary>How well one banner knows the hunter. A count of won contracts, never a balance.</summary>
public sealed record FactionRecord(FactionDefinition Faction, int WonHunts)
{
    public FactionStanding Standing => FactionStandings.TierFor(WonHunts);
}

/// <summary>A contract as every hunt route reports it: the promise, and what it is worth.</summary>
/// <param name="Monster">
/// Null only when the archetype it was written as has since left the catalog, which is the same
/// hole <see cref="Encounter.Monster"/> leaves and is handled the same way: the key is rendered
/// rather than a name, and nothing throws over a retired catalog entry (DEC-004).
/// </param>
/// <param name="Task">
/// Null when the task has been deleted. A discharged contract survives that, because everything
/// it is worth was frozen onto the contract row and is never read off the task again.
/// </param>
public sealed record HuntContractView(
    HuntContract Contract,
    MonsterDefinition? Monster,
    TodoTask? Task,
    FactionDefinition? Faction,
    FactionStanding Standing);

/// <summary>The board as a whole: what is on offer, what has been taken, and what it costs.</summary>
/// <param name="Offers">
/// Every task that could be written up, worst first, and deliberately not capped. The cap is a
/// display decision and lives on the display: a task card asks this same list whether its own
/// task carries a contract, and a list trimmed to twenty answers "no" for the twenty-first.
/// </param>
public sealed record HuntBoard(
    IReadOnlyList<HuntOffer> Offers,
    IReadOnlyList<HuntContractView> Contracts,
    IReadOnlyList<FactionRecord> Factions,
    int Stamina);

/// <summary>A contract in progress, with the fight it opened.</summary>
public sealed record HuntView(
    Encounter Encounter,
    HuntContract? Contract,
    TodoTask? Task,
    FactionDefinition? Faction,
    FactionStanding Standing);

/// <summary>
/// Writes tasks up as contracts, discharges them when the work is done, and sells the fight.
/// </summary>
/// <remarks>
/// Three steps, in this order, and the order is the whole feature:
/// <list type="number">
/// <item>
/// <b>Accept</b>, for nothing. No stamina, no encounter, no die. Charging to accept would be a
/// toll for having a backlog, which is the stick DEC-013 deleted.
/// </item>
/// <item>
/// <b>Discharge</b>, by finishing the task, and by nothing else. There is no route from an
/// unfinished task to bounty gold, loot or faction standing.
/// </item>
/// <item>
/// <b>Fight</b>, for one stamina, exactly as every other fight costs one (DEC-012).
/// </item>
/// </list>
/// The middle step is the one that was missing. A contract used to open a live encounter the
/// moment it was taken, which meant the monster could be killed while the task it stood for
/// stayed undone: the bounty on a neglected chore paid out for continuing to neglect it, and the
/// longer it was neglected the better it paid. DEC-013 exists to pull the player toward the
/// avoided task, so the fight is now downstream of the work rather than an alternative to it.
/// <para>
/// Nothing here writes Character.TotalXp, and nothing ever should. The two writes in the whole
/// repository are GamificationService's, and they stay there: a backlog pays gold, loot and
/// standing, and never the one number that compounds (DEC-012, DEC-013).
/// </para>
/// <para>
/// Just as deliberately, this service is not injected into <c>GamificationService</c> and never
/// should be. The completion transaction is the only explicit transaction in the tree, and the
/// guarantee that no hunt work runs inside it is structural rather than disciplinary: there is no
/// field, no constructor parameter and no using through which a later edit could pull hunt work
/// up above the commit. See TaskEndpoints.DischargeHuntAsync for the call site.
/// </para>
/// </remarks>
public sealed class HuntService(
    TodoDbContext db,
    CharacterSheetService sheets,
    CombatService combat)
{
    /// <summary>
    /// Every open task that could carry a contract, and every contract already taken.
    /// </summary>
    /// <remarks>
    /// Rolls nothing and writes nothing. The purse it quotes is the purse the fight will use,
    /// because both come from <see cref="HuntRules.StatBlock"/> over the same frozen inputs.
    /// </remarks>
    public async Task<HuntBoard> BoardAsync(Guid userId, CancellationToken cancellationToken)
    {
        var character = await db.Characters.SingleAsync(c => c.UserId == userId, cancellationToken);
        var sheet = await sheets.BuildAsync(character, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        // Parents only, spelled out as ParentId == null because IsProgressionBearing is
        // builder.Ignore'd and cannot reach the database. This is the server-side half of
        // DEC-014's gate; MayAwardAt is asked again in memory below, over the same rows.
        var candidates = await db.Tasks
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.ParentId == null)

            // The same predicate the task list's "open" filter uses, and it has to be: a
            // recurring task whose period has rolled over is stored Completed and is open again.
            .Where(t =>
                t.Status != TaskProgress.Completed ||
                (t.Recurrence != RecurrenceRule.None &&
                 t.XpEligibleFrom != null &&
                 t.XpEligibleFrom <= now))

            // One contract per task per window, derived rather than flagged. A contract accepted
            // in a previous period was accepted before that period's completion and does not
            // block the new one; a contract on a task that was completed and reopened still does,
            // because reopening nulls CompletedAt and the comparison falls back to CreatedAt.
            // That is the complete-reopen-reaccept farm closed structurally, with no column to
            // keep in step. Torn up contracts are exempt: one was never fought and never paid.
            .Where(t => !db.HuntContracts.Any(
                c => c.TaskId == t.Id
                    && c.Status != HuntContractStatus.Abandoned
                    && c.AcceptedAt >= (t.CompletedAt ?? t.CreatedAt)))

            .ToListAsync(cancellationToken);

        var huntable = candidates.Where(t => t.MayAwardAt(now) && !t.IsCompletedAt(now)).ToList();
        var subtasks = await SubtaskCountsAsync(userId, huntable, cancellationToken);
        var standings = await StandingsAsync(userId, cancellationToken);

        var offers = huntable
            .Select(task => Offer(task, sheet.Level, subtasks.GetValueOrDefault(task.Id), now, standings))

            // Worst first, because that is the one the game is trying to get finished. Ties break
            // on the fight rather than the calendar so the board has a stable order.
            .OrderByDescending(offer => offer.DaysOverdue)
            .ThenByDescending(offer => offer.Monster.MaxGold)
            .ThenBy(offer => offer.Task.SortOrder)
            .ThenBy(offer => offer.Task.CreatedAt)
            .ToList();

        var contracts = await LiveContractsAsync(userId, standings, cancellationToken);

        var factions = FactionCatalog.All
            .Select(faction => new FactionRecord(faction, standings.GetValueOrDefault(faction.Key)))
            .ToList();

        return new HuntBoard(offers, contracts, factions, character.Stamina);
    }

    /// <summary>
    /// Takes a contract on a task. Free, and the freedom is the point.
    /// </summary>
    /// <remarks>
    /// No stamina, no encounter row, no die: accepting writes one row and nothing else. Charging
    /// for it would be a toll on having a backlog, and DEC-013 replaced every such toll with a
    /// bounty. What it buys is the right to fight the thing once the task itself is done.
    /// </remarks>
    public async Task<RpgResult<HuntContractView>> AcceptAsync(
        Guid userId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var task = await db.Tasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, cancellationToken);

        // Somebody else's task answers exactly as one that never existed, so task ids cannot be
        // probed for existence through the adventure screen.
        if (task is null)
        {
            return RpgResult<HuntContractView>.Fail(RpgFailure.NotFound, "No such task.");
        }

        var now = DateTimeOffset.UtcNow;

        // DEC-014's single gate, reused verbatim rather than re-derived as ParentId == null. It
        // is false for a subtask, so splitting a task into twenty cannot mint twenty contracts,
        // and false for a recurring task inside its own period, so a daily can be written up once
        // a day rather than once a click.
        if (!task.MayAwardAt(now))
        {
            return RpgResult<HuntContractView>.Fail(
                RpgFailure.NotHuntable,
                task.IsProgressionBearing
                    ? "You have already dealt with that one this time round."
                    : "A subtask is part of a job, not a job. Take the contract on its parent.");
        }

        // A contract is taken on work that is still outstanding. A finished task has nothing left
        // to promise: writing one up afterwards and discharging it on the completion that already
        // happened would be a free fight bought with no new work.
        if (task.IsCompletedAt(now))
        {
            return RpgResult<HuntContractView>.Fail(
                RpgFailure.NotHuntable, "That one is already done. There is nothing left to hunt.");
        }

        var since = task.CompletedAt ?? task.CreatedAt;

        if (await db.HuntContracts.AnyAsync(
                c => c.TaskId == task.Id
                    && c.Status != HuntContractStatus.Abandoned
                    && c.AcceptedAt >= since,
                cancellationToken))
        {
            return RpgResult<HuntContractView>.Fail(
                RpgFailure.HuntAlreadyTaken, "That contract has already been taken.");
        }

        var character = await db.Characters.SingleAsync(c => c.UserId == userId, cancellationToken);
        var sheet = await sheets.BuildAsync(character, cancellationToken);

        var subtasks = await db.Tasks.CountAsync(t => t.ParentId == task.Id, cancellationToken);
        var contract = Write(userId, task, sheet.Level, subtasks, now);

        db.HuntContracts.Add(contract);
        await db.SaveChangesAsync(cancellationToken);

        var standings = await StandingsAsync(userId, cancellationToken);

        return RpgResult<HuntContractView>.Success(View(contract, task, standings));
    }

    /// <summary>
    /// Discharges the contract on a task that has just been finished, if there is one.
    /// </summary>
    /// <remarks>
    /// Called from the completion endpoints after GamificationService.CompleteAsync has returned,
    /// which is to say after its transaction has committed. Everything below therefore runs in its
    /// own unit of work and cannot roll a completion back, whatever it does. See
    /// TaskEndpoints.DischargeHuntAsync for the boundary itself.
    /// <para>
    /// Pays nothing. Discharging unlocks the fight and moves no gold, no loot, no standing and no
    /// quest, which is what makes it safe to run outside the completion's transaction: the worst a
    /// failure here can cost is a contract that has to be discharged again by pressing Done a
    /// second time.
    /// </para>
    /// <para>
    /// Returns null rather than a failure for every no-op, because pressing Done on a task with no
    /// contract is not an error and must not read as one.
    /// </para>
    /// </remarks>
    public async Task<HuntContractView?> DischargeAsync(
        Guid userId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var contract = await db.HuntContracts.FirstOrDefaultAsync(
            c => c.UserId == userId
                && c.TaskId == taskId
                && c.Status == HuntContractStatus.Accepted,
            cancellationToken);

        if (contract is null)
        {
            return null;
        }

        var task = await db.Tasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, cancellationToken);

        if (task is null || !IsDischargedBy(contract, task))
        {
            return null;
        }

        contract.Status = HuntContractStatus.Discharged;
        contract.DischargedAt = task.CompletedAt ?? DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        var standings = await StandingsAsync(userId, cancellationToken);

        return View(contract, task, standings);
    }

    /// <summary>
    /// Takes back the discharge on a task that has just been reopened, if there is one.
    /// </summary>
    /// <remarks>
    /// A discharged contract is the record of work that was done, and reopening says it was not.
    /// Without this the loop "finish it, undo it, then collect" would pay bounty gold, loot and
    /// standing on a task that ends the sequence unfinished, which is precisely the shape DEC-013
    /// exists to refuse: the reward has to stay attached to the work rather than to the click.
    /// <para>
    /// It costs the player nothing they had. The contract goes back to accepted and discharges
    /// again the moment the task is genuinely finished, because the acceptance moment it is
    /// measured against has not moved.
    /// </para>
    /// <para>
    /// A contract already fought is deliberately untouched, matching how a badge, a quest advance
    /// and spent stamina all survive a reopen: that fight happened.
    /// </para>
    /// </remarks>
    public async Task<HuntContractView?> UndischargeAsync(
        Guid userId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var contract = await db.HuntContracts.FirstOrDefaultAsync(
            c => c.UserId == userId
                && c.TaskId == taskId
                && c.Status == HuntContractStatus.Discharged,
            cancellationToken);

        if (contract is null)
        {
            return null;
        }

        contract.Status = HuntContractStatus.Accepted;
        contract.DischargedAt = null;

        await db.SaveChangesAsync(cancellationToken);

        var task = await db.Tasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, cancellationToken);

        var standings = await StandingsAsync(userId, cancellationToken);

        return View(contract, task, standings);
    }

    /// <summary>
    /// Opens the fight a discharged contract earned. One stamina, like every other fight.
    /// </summary>
    /// <remarks>
    /// The one door to a bounty, and it is shut until the work is done: a contract that is merely
    /// accepted is refused here, so there is no path from an unfinished task to bounty gold, loot
    /// or faction standing. Every gate is asked before
    /// <see cref="CombatService.StartAsync(Guid, HuntContract, CancellationToken)"/> is reached,
    /// so a refusal costs no stamina and shifts no die a retry would see. The stamina check, the
    /// one-fight-at-a-time refusal and the no-fight-inside-a-dungeon refusal are all that method's,
    /// unchanged and unduplicated.
    /// </remarks>
    public async Task<RpgResult<HuntView>> FightAsync(
        Guid userId,
        Guid contractId,
        CancellationToken cancellationToken)
    {
        // Tracked, not read-only, and that is load-bearing: CombatService closes it as it writes
        // the encounter, so the fight and the contract that bought it land in one save. A
        // contract left open beside a live fight would be one stamina away from a second bounty
        // for one piece of work.
        var contract = await db.HuntContracts.FirstOrDefaultAsync(
            c => c.Id == contractId && c.UserId == userId, cancellationToken);

        if (contract is null)
        {
            return RpgResult<HuntView>.Fail(RpgFailure.NotFound, "No such contract.");
        }

        if (contract.Status == HuntContractStatus.Accepted)
        {
            return RpgResult<HuntView>.Fail(
                RpgFailure.HuntNotDischarged,
                "The work is not done. Finish the task, and the contract is yours to collect on.");
        }

        if (!contract.MayBeFought)
        {
            return RpgResult<HuntView>.Fail(
                RpgFailure.HuntAlreadyFought, "That contract has already been collected on.");
        }

        var started = await combat.StartAsync(userId, contract, cancellationToken);

        // Forwarded rather than translated, which is how the stamina 422 and the
        // already-in-a-fight 409 arrive here intact and reach the client through the one existing
        // mapping site with no new failure member for either.
        if (!started.Ok)
        {
            return RpgResult<HuntView>.Fail(started.Failure, started.Message ?? string.Empty);
        }

        return RpgResult<HuntView>.Success(
            await ViewAsync(started.Value!, contract, cancellationToken));
    }

    /// <summary>Tears up a live contract. Free, and it forfeits nothing that was paid for.</summary>
    /// <remarks>
    /// The way out, and the way to have a contract re-priced after the task underneath it has
    /// genuinely changed shape: what was frozen at acceptance stays frozen, so tearing up and
    /// taking it again is the only way to re-read the task. Abandoning cannot be turned into a
    /// gain, because a fresh contract can only be discharged by a completion that postdates it.
    /// </remarks>
    public async Task<RpgResult<HuntContractView>> AbandonAsync(
        Guid userId,
        Guid contractId,
        CancellationToken cancellationToken)
    {
        var contract = await db.HuntContracts.FirstOrDefaultAsync(
            c => c.Id == contractId && c.UserId == userId, cancellationToken);

        if (contract is null)
        {
            return RpgResult<HuntContractView>.Fail(RpgFailure.NotFound, "No such contract.");
        }

        if (!contract.IsLive)
        {
            return RpgResult<HuntContractView>.Fail(
                RpgFailure.HuntAlreadyFought, "That contract is already closed.");
        }

        contract.Status = HuntContractStatus.Abandoned;
        contract.ClosedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        var task = contract.TaskId is { } taskId
            ? await db.Tasks.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, cancellationToken)
            : null;

        var standings = await StandingsAsync(userId, cancellationToken);

        return RpgResult<HuntContractView>.Success(View(contract, task, standings));
    }

    /// <summary>The contract fight in progress, or null. Rolls nothing.</summary>
    public async Task<HuntView?> ActiveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var encounter = await db.Encounters
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.UserId == userId && e.Status == EncounterStatus.Active, cancellationToken);

        // IsHunt rather than a TaskId test, because a contract whose task has been deleted is
        // still a contract: it still derives its block, still shows the name it was fought under
        // and still counts toward its banner.
        return encounter is null || !encounter.IsHunt
            ? null
            : await ViewAsync(encounter, contract: null, cancellationToken);
    }

    /// <summary>
    /// Whether the completion on this row is the one that discharges this contract.
    /// </summary>
    /// <remarks>
    /// Three facts, and the third is the one that makes the other two safe to trust.
    /// <para>
    /// <c>IsCompletedAt</c> is the work being done, asked of the row rather than taken from the
    /// caller. <c>XpAwarded</c> is DEC-014's own answer, snapshotted by the completion that asked
    /// it: it is written as <c>awards ? BaseXp : 0</c> and zeroed again by a reopen, so a
    /// completion that paid nothing discharges nothing.
    /// </para>
    /// <para>
    /// <c>CompletedAt >= AcceptedAt</c> is the third, and without it the first two are two
    /// snapshots that an ordinary edit can make stale-but-true. A daily task completed in a
    /// previous period keeps <c>XpAwarded = 25</c> and a stored <c>Completed</c> that only
    /// <c>StatusAt</c>'s recurrence branch masks back to Todo; setting the task's repeat to None
    /// afterwards removes the mask and both read true with no work done in the current window.
    /// Comparing the completion against the moment the contract was accepted refuses that
    /// outright: a contract can only be accepted while the task reads open, so any completion
    /// already on the row when it was written predates it and cannot discharge it.
    /// </para>
    /// <para>
    /// MayAwardAt deliberately is not asked a second time here, and could not be: a paying
    /// completion of a recurring task is exactly what closes that gate, so asking it after the
    /// fact would refuse every recurring contract at the moment it was earned.
    /// </para>
    /// </remarks>
    private static bool IsDischargedBy(HuntContract contract, TodoTask task) =>
        task.IsCompletedAt(DateTimeOffset.UtcNow)
        && task.XpAwarded > 0
        && task.CompletedAt is { } completedAt
        && completedAt >= contract.AcceptedAt;

    /// <summary>
    /// Writes a task up as a contract. Pure, and pure in the sense that matters: no die.
    /// </summary>
    /// <remarks>
    /// The one place the arithmetic lives, so the board quotes exactly what the fight will use.
    /// Two copies would drift, and the first thing to drift would be the purse, which is the one
    /// number the player made the decision on.
    /// </remarks>
    private static HuntContract Write(
        Guid userId,
        TodoTask task,
        int characterLevel,
        int subtasks,
        DateTimeOffset now)
    {
        var daysOverdue = DaysOverdueFor(task, now);

        return new HuntContract
        {
            UserId = userId,
            TaskId = task.Id,
            TaskTitle = task.Title,
            ArchetypeKey = HuntArchetypeCatalog.ShapeFor(task.Difficulty, subtasks, daysOverdue).Key,
            Level = HuntRules.LevelFor(characterLevel, task.Difficulty),
            DaysOverdue = daysOverdue,
            Subtasks = subtasks,

            // The catalog key, never the tag string, so a user's casing never reaches the
            // database: however "WORK" was typed, what is stored is "the-ledger".
            FactionKey = FactionCatalog.Find(FactionCatalog.FactionFor(task))?.Key,
            Status = HuntContractStatus.Accepted,
            AcceptedAt = now
        };
    }

    /// <summary>
    /// How overdue a task is for the purpose of a contract, which is not always what
    /// <see cref="TodoTask.DaysOverdue"/> reports.
    /// </summary>
    /// <remarks>
    /// A recurring task's due date is never advanced by recurrence. CompleteAsync writes Status,
    /// CompletedAt, the two snapshots and XpEligibleFrom, and never touches DueDate, so "water
    /// the plants", daily, due a year ago and completed faithfully every single day, reports 365
    /// days overdue forever. A bounty keyed off that would pay the best maintained task on the
    /// list the largest purse in the game, which is the exact opposite of what DEC-013 is for.
    /// <para>
    /// So a recurring task is measured from the gate that reopened it, which is the moment it
    /// genuinely became due again. One that has never been completed has no gate and falls back
    /// to its due date, which is the truth: it has never been done. When both exist the later one
    /// wins, so a weekly task whose stated due date is still ahead is not overdue for having
    /// rolled over.
    /// </para>
    /// <para>
    /// Truncating, absolute UTC and never negative, matching TodoTask.DaysOverdue at both ends so
    /// the board and the task card agree on every non-recurring row.
    /// </para>
    /// </remarks>
    private static int DaysOverdueFor(TodoTask task, DateTimeOffset now)
    {
        var due = task.DueDate;

        if (task.Recurrence != RecurrenceRule.None && task.XpEligibleFrom is { } reopened)
        {
            due = due is { } stated && stated > reopened ? stated : reopened;
        }

        return due is { } deadline && now > deadline ? (int)(now - deadline).TotalDays : 0;
    }

    private static HuntOffer Offer(
        TodoTask task,
        int characterLevel,
        int subtasks,
        DateTimeOffset now,
        IReadOnlyDictionary<string, int> standings)
    {
        // Written and thrown away rather than persisted: quoting the board through the same
        // method acceptance uses is what stops the board advertising a fight the contract would
        // not open.
        var quoted = Write(Guid.Empty, task, characterLevel, subtasks, now);
        var faction = FactionCatalog.Find(quoted.FactionKey);

        return new HuntOffer(
            task,
            quoted.ArchetypeKey,
            quoted.Level,
            quoted.DaysOverdue,
            quoted.Subtasks,
            quoted.Monster!,
            faction,
            StandingFor(faction, standings));
    }

    /// <summary>The live contracts on the board, worst first, with their blocks derived.</summary>
    private async Task<List<HuntContractView>> LiveContractsAsync(
        Guid userId,
        IReadOnlyDictionary<string, int> standings,
        CancellationToken cancellationToken)
    {
        var live = await db.HuntContracts
            .AsNoTracking()
            .Where(c => c.UserId == userId
                && (c.Status == HuntContractStatus.Accepted
                    || c.Status == HuntContractStatus.Discharged))
            .OrderByDescending(c => c.Status)
            .ThenByDescending(c => c.DaysOverdue)
            .ThenBy(c => c.AcceptedAt)
            .ToListAsync(cancellationToken);

        if (live.Count == 0)
        {
            return [];
        }

        var taskIds = live.Where(c => c.TaskId is not null).Select(c => c.TaskId!.Value).ToList();

        var tasks = await db.Tasks
            .AsNoTracking()
            .Where(t => t.UserId == userId && taskIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        return [.. live.Select(contract => View(
            contract,
            contract.TaskId is { } id ? tasks.GetValueOrDefault(id) : null,
            standings))];
    }

    private static HuntContractView View(
        HuntContract contract,
        TodoTask? task,
        IReadOnlyDictionary<string, int> standings)
    {
        var faction = FactionCatalog.Find(contract.FactionKey);

        return new HuntContractView(
            contract, contract.Monster, task, faction, StandingFor(faction, standings));
    }

    private static FactionStanding StandingFor(
        FactionDefinition? faction,
        IReadOnlyDictionary<string, int> standings) =>
        faction is null
            ? FactionStanding.Unknown
            : FactionStandings.TierFor(standings.GetValueOrDefault(faction.Key));

    /// <summary>Reads back the derived things a contract's fight is reported with.</summary>
    private async Task<HuntView> ViewAsync(
        Encounter encounter,
        HuntContract? contract,
        CancellationToken cancellationToken)
    {
        var faction = FactionCatalog.Find(encounter.HuntFactionKey);

        var standing = faction is null
            ? FactionStanding.Unknown
            : FactionStandings.TierFor(
                await WonUnderBannerAsync(encounter.UserId, faction.Key, cancellationToken));

        contract ??= await db.HuntContracts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.EncounterId == encounter.Id && c.UserId == encounter.UserId,
                cancellationToken);

        var task = encounter.TaskId is { } taskId
            ? await db.Tasks
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    t => t.Id == taskId && t.UserId == encounter.UserId, cancellationToken)
            : null;

        return new HuntView(encounter, contract, task, faction, standing);
    }

    /// <summary>
    /// Standing under one banner: a count of won contracts, never a stored counter (DEC-002).
    /// </summary>
    /// <remarks>
    /// Served by IX_encounters_UserId_HuntFactionKey_Status as an index only scan. Counting wins
    /// rather than contracts taken is what makes an accepted or fled contract worth no standing,
    /// and counting at all rather than storing is what makes it impossible for standing to
    /// disagree with the fights on the table.
    /// </remarks>
    private Task<int> WonUnderBannerAsync(
        Guid userId,
        string factionKey,
        CancellationToken cancellationToken) =>
        db.Encounters.CountAsync(
            e => e.UserId == userId
                && e.HuntFactionKey == factionKey
                && e.Status == EncounterStatus.Won,
            cancellationToken);

    /// <summary>Every banner's standing in one query, for the board.</summary>
    private async Task<Dictionary<string, int>> StandingsAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await db.Encounters
            .Where(e => e.UserId == userId
                && e.Status == EncounterStatus.Won
                && e.HuntFactionKey != null)
            .GroupBy(e => e.HuntFactionKey!)
            .Select(group => new { Key = group.Key, Won = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Won, StringComparer.Ordinal, cancellationToken);

    /// <summary>
    /// How many subtasks each candidate carries, in one round trip.
    /// </summary>
    /// <remarks>
    /// Counted rather than loaded, because the shape only wants the number, and scoped by
    /// UserId as well as ParentId so a hand-crafted parent id cannot count another user's rows.
    /// </remarks>
    private async Task<Dictionary<Guid, int>> SubtaskCountsAsync(
        Guid userId,
        IReadOnlyList<TodoTask> parents,
        CancellationToken cancellationToken)
    {
        if (parents.Count == 0)
        {
            return [];
        }

        var ids = parents.Select(t => t.Id).ToList();

        return await db.Tasks
            .Where(t => t.UserId == userId && t.ParentId != null && ids.Contains(t.ParentId.Value))
            .GroupBy(t => t.ParentId!.Value)
            .Select(group => new { ParentId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.ParentId, row => row.Count, cancellationToken);
    }
}
