using System.Reflection;
using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Services;
using TodoApp.Api.Services.Rpg;
using TodoApp.Data;
using TodoApp.Models;
using TodoApp.Models.Dice;
using TodoApp.Models.Progression;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

/// <summary>
/// Contracts against a real database, wired the way production wires them.
/// </summary>
/// <remarks>
/// One <see cref="TodoDbContext"/> shared by every service, which is what the scoped
/// registrations give them per request. That sharing is the trap the whole transaction boundary
/// is arranged around: <c>SaveChangesAsync</c> flushes the entire change tracker, so a hunt
/// entity tracked before the completion commits would ride inside its transaction however
/// innocent the call site looked. A harness that gave each service its own context would test a
/// shape production does not have.
/// <para>
/// The lifecycle every test below is written against has three steps and they are not
/// interchangeable. Accepting a contract is free. Finishing the task discharges it, and nothing
/// else does. Only then can it be fought, for the one stamina every fight costs. The step that
/// does not exist is the important one: there is no route from an unfinished task to bounty gold,
/// loot or faction standing, because a bounty that could be collected by fighting instead of by
/// finishing would pay the player to go on avoiding the task DEC-013 wrote it up to get done.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class HuntContractTests(PostgresFixture postgres)
{
    /// <summary>
    /// Enough scripted twenties to carry any test in this file to its conclusion.
    /// </summary>
    /// <remarks>
    /// Long rather than tight on purpose. Every contract is now fought rather than settled on
    /// paper, so a test that runs three of them through to a kill draws several times what one
    /// that settled them by completion ever did. It is a repeat rather than a script of exact
    /// faces, so its length pins nothing.
    /// </remarks>
    private static SequenceDiceRoller AlwaysHits() => new(Enumerable.Repeat(20, 4000).ToArray());

    private sealed record Harness(
        TodoDbContext Db,
        CombatService Combat,
        HuntService Hunts,
        QuestService Quests,
        LootService Loot,
        AdventurerService Adventurer,
        GamificationService Gamification,
        Guid UserId);

    private async Task<Harness> ArrangeAsync(IDiceRoller roller, int level = 3, int stamina = 20)
    {
        await postgres.ResetAsync();
        var user = await postgres.CreateUserAsync("test|hunter");

        var db = postgres.CreateContext();
        var sheets = new CharacterSheetService(db);
        var loot = new LootService(db, roller);
        var quests = new QuestService(db, loot);
        var adventurer = new AdventurerService(db, sheets, loot);
        var combat = new CombatService(db, roller, sheets, loot, quests);
        var hunts = new HuntService(db, sheets, combat);
        var gamification = new GamificationService(db, new AchievementEvaluator(), quests);

        await adventurer.ChooseClassAsync(
            user.Id, ClassCatalog.Fighter, TestContext.Current.CancellationToken);

        var harness = new Harness(
            db, combat, hunts, quests, loot, adventurer, gamification, user.Id);

        await ReachLevelAsync(harness, level);

        var character = await db.Characters.SingleAsync(c => c.UserId == user.Id);
        character.Stamina = stamina;
        await db.SaveChangesAsync();

        return harness;
    }

    /// <summary>
    /// Raises the character the only way anything is allowed to, by finishing real work.
    /// </summary>
    /// <remarks>
    /// Nothing in the RPG layer may pay experience (DEC-012), and a test that reached in and
    /// assigned TotalXp would be the first thing in the repository to write it.
    /// </remarks>
    private static async Task ReachLevelAsync(Harness harness, int level)
    {
        while (true)
        {
            var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);

            if (LevelCurve.LevelForXp(character.TotalXp) >= level)
            {
                return;
            }

            var task = new TodoTask
            {
                UserId = harness.UserId,
                Title = "Real work",
                Difficulty = Difficulty.Epic
            };

            harness.Db.Tasks.Add(task);
            await harness.Db.SaveChangesAsync();

            await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);
        }
    }

    private static async Task<TodoTask> AddTaskAsync(
        Harness harness,
        string title = "File the tax return",
        Difficulty difficulty = Difficulty.Hard,
        int daysOverdue = 0,
        string[]? tags = null,
        RecurrenceRule recurrence = RecurrenceRule.None,
        Guid? parentId = null)
    {
        var task = new TodoTask
        {
            UserId = harness.UserId,
            ParentId = parentId,
            Title = title,
            Difficulty = difficulty,
            Recurrence = recurrence,
            Tags = [.. tags ?? []],

            // An extra hour past the whole days, because DaysOverdue truncates.
            DueDate = daysOverdue > 0
                ? DateTimeOffset.UtcNow.AddDays(-daysOverdue).AddHours(-1)
                : null
        };

        harness.Db.Tasks.Add(task);
        await harness.Db.SaveChangesAsync();

        return task;
    }

    private static Task<Character> CharacterAsync(Harness harness) =>
        harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId, TestContext.Current.CancellationToken);

    /// <summary>Won contracts under one banner, counted exactly as standing is.</summary>
    private static Task<int> StandingAsync(Harness harness, string factionKey) =>
        harness.Db.Encounters.CountAsync(
            e => e.UserId == harness.UserId
                && e.HuntFactionKey == factionKey
                && e.Status == EncounterStatus.Won,
            TestContext.Current.CancellationToken);

    /// <summary>Takes the contract, asserting it was allowed, and hands back the row.</summary>
    private static async Task<HuntContract> AcceptAsync(Harness harness, Guid taskId)
    {
        var accepted = await harness.Hunts.AcceptAsync(harness.UserId, taskId, default);

        Assert.True(accepted.Ok, accepted.Message);

        return accepted.Value!.Contract;
    }

    /// <summary>The contract row as the database currently holds it.</summary>
    private static Task<HuntContract> ContractAsync(Harness harness, Guid contractId) =>
        harness.Db.HuntContracts.AsNoTracking()
            .SingleAsync(c => c.Id == contractId, TestContext.Current.CancellationToken);

    /// <summary>Accept, finish the work, and take the fight. The whole honest sequence.</summary>
    private static async Task<HuntView> RunTheWholeWayAsync(Harness harness, Guid taskId)
    {
        var contract = await AcceptAsync(harness, taskId);

        await harness.Gamification.CompleteAsync(harness.UserId, taskId, 0, default);

        Assert.NotNull(await harness.Hunts.DischargeAsync(harness.UserId, taskId, default));

        var fight = await harness.Hunts.FightAsync(harness.UserId, contract.Id, default);

        Assert.True(fight.Ok, fight.Message);

        return fight.Value!;
    }

    /// <summary>
    /// Swings until the thing stops moving, keeping the hunter on their feet in between.
    /// </summary>
    /// <remarks>
    /// The scripted twenties hit for everybody, the monster included, so a hunter who fought three
    /// contracts back to back would be carried out before the third and a Dread written on a
    /// year-old Epic would win outright. What these tests are about is what a win pays and what a
    /// contract costs, never whether this particular fighter survives, so the hit points are
    /// topped up between rounds rather than the assertions being softened to accept a loss.
    /// </remarks>
    private static async Task<AttackOutcome> WinTheFightAsync(Harness harness, Guid encounterId)
    {
        var sheets = new CharacterSheetService(harness.Db);

        for (var round = 0; round < 200; round++)
        {
            var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);
            var sheet = await sheets.BuildAsync(character, default);

            character.CurrentHitPoints = sheet.MaxHitPoints;
            character.HitPointsUpdatedAt = DateTimeOffset.UtcNow;
            await harness.Db.SaveChangesAsync();

            var attack = await harness.Combat.AttackAsync(harness.UserId, encounterId, default);

            Assert.True(attack.Ok);

            if (attack.Value!.Encounter.IsOver)
            {
                Assert.Equal(EncounterStatus.Won, attack.Value.Encounter.Status);

                return attack.Value;
            }
        }

        throw new Xunit.Sdk.XunitException(
            "Two hundred rounds of guaranteed hits did not finish the fight.");
    }

    // -------------------------------------------------------------------------
    // The rule the whole phase exists for: the work first, the bounty after.
    // -------------------------------------------------------------------------

    /// <summary>
    /// A contract cannot be fought while its task is unfinished, by any route.
    /// </summary>
    /// <remarks>
    /// The headline rule. A contract used to open a live encounter the moment it was taken, so
    /// the creature could be killed while the chore it stood for went on being ignored: the
    /// bounty on a neglected task paid out for continuing to neglect it, and the longer it was
    /// neglected the better it paid. DEC-013 wrote the bounty to pull the player toward the
    /// avoided task, so the fight is downstream of the work and there is no door round it.
    /// </remarks>
    [Fact]
    public async Task A_contract_cannot_be_fought_while_its_task_is_unfinished()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var task = await AddTaskAsync(harness, "Left for a month", daysOverdue: 30, tags: ["work"]);
        var contract = await AcceptAsync(harness, task.Id);

        var before = await CharacterAsync(harness);

        var refused = await harness.Hunts.FightAsync(harness.UserId, contract.Id, default);

        Assert.False(refused.Ok);
        Assert.Equal(RpgFailure.HuntNotDischarged, refused.Failure);

        // Refused before a stamina is spent and before a fight is written, so a retry loop
        // costs nothing and leaves nothing behind.
        var after = await CharacterAsync(harness);

        Assert.Equal(before.Stamina, after.Stamina);
        Assert.Equal(before.Gold, after.Gold);
        Assert.False(await harness.Db.Encounters.AnyAsync(e => e.UserId == harness.UserId));
        Assert.Equal(0, await StandingAsync(harness, FactionCatalog.TheLedger));

        // And the refusal is about the work rather than about the contract: doing it opens the
        // fight through the very same call.
        await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);
        Assert.NotNull(await harness.Hunts.DischargeAsync(harness.UserId, task.Id, default));

        var allowed = await harness.Hunts.FightAsync(harness.UserId, contract.Id, default);

        Assert.True(allowed.Ok);
        Assert.True(allowed.Value!.Encounter.IsHunt);
    }

    /// <summary>
    /// No route pays bounty gold, loot or standing for a task that is not finished.
    /// </summary>
    /// <remarks>
    /// The rule above, asserted over every door rather than over the one that used to be open.
    /// Taking the contract, pressing Done on a different task, ticking off a subtask beneath it
    /// and asking for the fight all leave the purse exactly where it was, because a bounty is
    /// what finishing pays.
    /// </remarks>
    [Fact]
    public async Task No_route_pays_bounty_gold_for_an_incomplete_task()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var task = await AddTaskAsync(harness, "The worst one", Difficulty.Epic, 200, ["work"]);
        var child = await AddTaskAsync(harness, "A piece of it", parentId: task.Id);
        var elsewhere = await AddTaskAsync(harness, "Something easy", Difficulty.Easy);

        var contract = await AcceptAsync(harness, task.Id);
        var before = await CharacterAsync(harness);
        var itemsBefore = await harness.Db.InventoryItems.CountAsync(i => i.UserId == harness.UserId);

        // Every door, one after another.
        Assert.False((await harness.Hunts.FightAsync(harness.UserId, contract.Id, default)).Ok);

        await harness.Gamification.CompleteAsync(harness.UserId, child.Id, 0, default);
        Assert.Null(await harness.Hunts.DischargeAsync(harness.UserId, child.Id, default));

        await harness.Gamification.CompleteAsync(harness.UserId, elsewhere.Id, 0, default);
        Assert.Null(await harness.Hunts.DischargeAsync(harness.UserId, elsewhere.Id, default));

        Assert.Null(await harness.Hunts.DischargeAsync(harness.UserId, task.Id, default));
        Assert.False((await harness.Hunts.FightAsync(harness.UserId, contract.Id, default)).Ok);

        var after = await CharacterAsync(harness);

        Assert.Equal(before.Gold, after.Gold);
        Assert.Equal(
            itemsBefore,
            await harness.Db.InventoryItems.CountAsync(i => i.UserId == harness.UserId));
        Assert.Equal(0, await StandingAsync(harness, FactionCatalog.TheLedger));
        Assert.False(await harness.Db.Encounters.AnyAsync(e => e.UserId == harness.UserId));

        // The task is exactly as unfinished as it was, which is the whole complaint the old
        // shape answered wrongly.
        var untouched = await harness.Db.Tasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);

        Assert.Equal(TaskProgress.Todo, untouched.Status);
        Assert.Equal(HuntContractStatus.Accepted, (await ContractAsync(harness, contract.Id)).Status);
    }

    /// <summary>
    /// Taking a contract is free, in every currency the game has.
    /// </summary>
    /// <remarks>
    /// Charging to accept would be a toll for having a backlog, and a toll on a backlog is
    /// exactly the stick DEC-013 deleted. It writes one row and touches nothing else: not
    /// stamina, not gold, not hit points, not experience, and no encounter.
    /// </remarks>
    [Fact]
    public async Task Taking_a_contract_costs_nothing_at_all()
    {
        var harness = await ArrangeAsync(AlwaysHits(), stamina: 0);

        var task = await AddTaskAsync(harness, "Left for a year", Difficulty.Epic, 365, ["work"]);

        var before = await CharacterAsync(harness);
        var itemsBefore = await harness.Db.InventoryItems.CountAsync(i => i.UserId == harness.UserId);

        // With nothing at all in the tank, which is the sharpest reading of "free": the player
        // with no stamina is the one a toll would shut out of their own backlog.
        var contract = await AcceptAsync(harness, task.Id);

        var after = await CharacterAsync(harness);

        Assert.Equal(0, after.Stamina);
        Assert.Equal(before.Gold, after.Gold);
        Assert.Equal(before.TotalXp, after.TotalXp);
        Assert.Equal(before.CurrentHitPoints, after.CurrentHitPoints);
        Assert.Equal(
            itemsBefore,
            await harness.Db.InventoryItems.CountAsync(i => i.UserId == harness.UserId));

        Assert.False(await harness.Db.Encounters.AnyAsync(e => e.UserId == harness.UserId));
        Assert.Equal(HuntContractStatus.Accepted, (await ContractAsync(harness, contract.Id)).Status);
        Assert.Null(contract.EncounterId);
    }

    /// <summary>
    /// Finishing the task is the only thing that discharges a contract.
    /// </summary>
    [Fact]
    public async Task Only_finishing_the_task_discharges_a_contract()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var task = await AddTaskAsync(harness, daysOverdue: 12, tags: ["work"]);
        var contract = await AcceptAsync(harness, task.Id);

        // Reading the board, taking other contracts and swinging at the tavern all leave it
        // where it is.
        await harness.Hunts.BoardAsync(harness.UserId, default);
        await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);

        var tavern = await harness.Combat.ActiveAsync(harness.UserId, default);

        Assert.NotNull(tavern);
        await WinTheFightAsync(harness, tavern.Id);

        Assert.Equal(HuntContractStatus.Accepted, (await ContractAsync(harness, contract.Id)).Status);

        var completed = await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);

        Assert.NotNull(completed);
        Assert.Equal(Difficulty.Hard.BaseXp(), completed.XpGained);

        var discharged = await harness.Hunts.DischargeAsync(harness.UserId, task.Id, default);

        Assert.NotNull(discharged);
        Assert.Equal(HuntContractStatus.Discharged, discharged.Contract.Status);
        Assert.NotNull(discharged.Contract.DischargedAt);

        var row = await ContractAsync(harness, contract.Id);

        Assert.Equal(HuntContractStatus.Discharged, row.Status);
        Assert.True(row.DischargedAt >= row.AcceptedAt);
    }

    /// <summary>
    /// Discharging pays nothing, which is what makes it safe outside the completion's
    /// transaction.
    /// </summary>
    /// <remarks>
    /// This is where the whole ordering argument now rests. Settling a contract used to pay
    /// gold, roll a drop, roll a faction reward and advance quests, all after the completion had
    /// committed and outside its transaction, which is why a thrown die there had to be proved
    /// harmless. It no longer pays anything: it writes one status and one timestamp, and the
    /// purse stays inside the creature until somebody spends a stamina on it.
    /// <para>
    /// Asserted with the dice tray armed to throw on the very next roll, so any hunt work that
    /// crept back into the completion path would take this test down rather than hide in it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Discharging_a_contract_pays_nothing_and_rolls_nothing()
    {
        var dice = new FailingDiceRoller(AlwaysHits());
        var harness = await ArrangeAsync(dice);

        var task = await AddTaskAsync(harness, daysOverdue: 30, tags: ["work"]);
        var contract = await AcceptAsync(harness, task.Id);

        var before = await CharacterAsync(harness);
        var itemsBefore = await harness.Db.InventoryItems.CountAsync(i => i.UserId == harness.UserId);

        dice.Armed = true;

        var completed = await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);

        Assert.NotNull(completed);
        Assert.NotNull(await harness.Hunts.DischargeAsync(harness.UserId, task.Id, default));

        dice.Armed = false;

        var after = await CharacterAsync(harness);

        // The task paid its own XP and stamina. The contract paid nothing.
        Assert.Equal(before.TotalXp + Difficulty.Hard.BaseXp(), after.TotalXp);
        Assert.Equal(before.Stamina + Difficulty.Hard.Stamina(), after.Stamina);
        Assert.Equal(before.Gold, after.Gold);
        Assert.Equal(
            itemsBefore,
            await harness.Db.InventoryItems.CountAsync(i => i.UserId == harness.UserId));
        Assert.Equal(0, await StandingAsync(harness, FactionCatalog.TheLedger));
        Assert.False(await harness.Db.Encounters.AnyAsync(e => e.UserId == harness.UserId));

        // And the completion is committed and visible from outside this context before the
        // contract is touched at all, which is the ordering the boundary depends on.
        await using var fresh = postgres.CreateContext();

        var reread = await fresh.Tasks.SingleAsync(t => t.Id == task.Id);

        Assert.Equal(TaskProgress.Completed, reread.Status);
        Assert.Equal(Difficulty.Hard.BaseXp(), reread.XpAwarded);
        Assert.Equal(
            HuntContractStatus.Discharged,
            (await fresh.HuntContracts.SingleAsync(c => c.Id == contract.Id)).Status);
    }

    /// <summary>
    /// The boundary is structural rather than disciplinary, and this is what makes it so.
    /// </summary>
    /// <remarks>
    /// <c>GamificationService</c> owns the only explicit transaction in the tree. It has no field,
    /// no constructor parameter and no property through which a later edit could reach hunt work,
    /// so pulling a discharge above the commit would require adding a dependency here first, and
    /// this test is where that shows up. The reverse direction is asserted too: a HuntService that
    /// could call back into CompleteAsync would reopen the same hole from the other side.
    /// </remarks>
    [Fact]
    public void The_completion_service_is_given_no_way_to_reach_a_contract()
    {
        var forbidden = new[] { typeof(HuntService), typeof(CombatService), typeof(LootService) };

        var parameters = typeof(GamificationService)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

        Assert.NotEmpty(parameters);
        Assert.All(forbidden, type => Assert.DoesNotContain(type, parameters));

        var fields = typeof(GamificationService)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(f => f.FieldType)
            .ToList();

        Assert.All(forbidden, type => Assert.DoesNotContain(type, fields));

        // Nothing named for the feature either, which catches a wrapper or an interface that
        // smuggled the same reach in under another type.
        Assert.DoesNotContain(
            typeof(GamificationService)
                .GetMembers(BindingFlags.Instance | BindingFlags.Static
                    | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(m => m.Name),
            name => name.Contains("Hunt", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(
            typeof(HuntService)
                .GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Select(p => p.ParameterType),
            type => type == typeof(GamificationService));
    }

    // -------------------------------------------------------------------------
    // DEC-014, the single progression gate, and the side doors into it.
    // -------------------------------------------------------------------------

    /// <summary>
    /// A completion from another window can never discharge a contract, however it is dressed up.
    /// </summary>
    /// <remarks>
    /// The defect this closes, and it was reachable with no adversarial intent through ordinary
    /// screens. A daily task completed in a previous period keeps <c>XpAwarded = 25</c> and a
    /// stored <c>Completed</c> that only <c>StatusAt</c>'s recurrence branch masks back to Todo.
    /// Setting the task's repeat to None afterwards, which the edit form allows on a completed
    /// parent and which deliberately leaves the two snapshots alone, removes the mask: both read
    /// true with no work done in the current window, and a settlement keyed on them alone paid
    /// the contract in full.
    /// <para>
    /// The fix is the third fact. A contract can only be taken while its task reads open, so any
    /// completion already on the row when it was accepted predates it; comparing the two refuses
    /// a discharge that is being answered by a snapshot from a different window. The two writes
    /// below are exactly the two <c>TaskEndpoints.UpdateTask</c> performs.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_completion_from_a_previous_period_can_never_discharge_a_contract()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var task = await AddTaskAsync(
            harness, "Water the plants", Difficulty.Medium, daysOverdue: 9,
            tags: ["work"], recurrence: RecurrenceRule.Daily);

        // Period one, done honestly. This is the completion that will be dressed up as current.
        await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);

        var paid = await harness.Db.Tasks.SingleAsync(t => t.Id == task.Id);

        Assert.Equal(Difficulty.Medium.BaseXp(), paid.XpAwarded);

        // The successor is the row a contract can be taken on now; the one just completed is
        // finished for good (DEC-015). Found through the link the completion wrote, which is
        // exact where a title match would not be.
        Assert.NotNull(paid.SpawnedTaskId);

        var successor = await harness.Db.Tasks.SingleAsync(t => t.Id == paid.SpawnedTaskId);

        var contract = await AcceptAsync(harness, successor.Id);
        var before = await CharacterAsync(harness);

        // A completion that predates the contract, written on by hand and dressed to look
        // exactly like a current one. Under the old model an ordinary edit could produce this
        // state; it cannot now, which is precisely why the guard is worth a test of its own
        // rather than being left to look like dead weight.
        var edited = await harness.Db.Tasks.SingleAsync(t => t.Id == successor.Id);

        edited.Status = TaskProgress.Completed;
        edited.CompletedAt = contract.AcceptedAt.AddHours(-1);
        edited.XpAwarded = Difficulty.Medium.BaseXp();
        edited.UpdatedAt = DateTimeOffset.UtcNow;
        await harness.Db.SaveChangesAsync();

        // The row now lies in exactly the way the first two facts believe: complete, and paid.
        Assert.True(edited.IsCompleted);
        Assert.True(edited.XpAwarded > 0);

        // Both doors into a settlement, and neither opens.
        Assert.Null(await harness.Hunts.DischargeAsync(harness.UserId, successor.Id, default));

        var refused = await harness.Hunts.FightAsync(harness.UserId, contract.Id, default);

        Assert.False(refused.Ok);
        Assert.Equal(RpgFailure.HuntNotDischarged, refused.Failure);

        var after = await CharacterAsync(harness);

        Assert.Equal(before.Gold, after.Gold);
        Assert.Equal(before.TotalXp, after.TotalXp);
        Assert.Equal(before.Stamina, after.Stamina);
        Assert.Equal(0, await StandingAsync(harness, FactionCatalog.TheLedger));
        Assert.False(await harness.Db.Encounters.AnyAsync(e => e.UserId == harness.UserId));
        Assert.Equal(HuntContractStatus.Accepted, (await ContractAsync(harness, contract.Id)).Status);
    }

    /// <summary>
    /// A subtask is part of a job, not a job, so it can never be written up (DEC-014).
    /// </summary>
    /// <remarks>
    /// Splitting one task into twenty would otherwise mint twenty contracts, each with its own
    /// purse, out of the same afternoon's work. The gate is <c>MayAwardAt</c> itself rather than a
    /// re-derived ParentId check, so it cannot fall out of step with the one the completion asks.
    /// </remarks>
    [Fact]
    public async Task A_subtask_can_never_be_written_up_as_a_contract()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var parent = await AddTaskAsync(harness, daysOverdue: 40, tags: ["work"]);
        var child = await AddTaskAsync(harness, "A piece of it", parentId: parent.Id, daysOverdue: 40);

        var refused = await harness.Hunts.AcceptAsync(harness.UserId, child.Id, default);

        Assert.False(refused.Ok);
        Assert.Equal(RpgFailure.NotHuntable, refused.Failure);
        Assert.False(await harness.Db.HuntContracts.AnyAsync(c => c.TaskId == child.Id));

        // And it is not on the board either, however overdue it is.
        var board = await harness.Hunts.BoardAsync(harness.UserId, default);

        Assert.DoesNotContain(board.Offers, offer => offer.Task.Id == child.Id);
        Assert.Contains(board.Offers, offer => offer.Task.Id == parent.Id);
    }

    /// <summary>
    /// The side door: a live contract on a parent, and a subtask ticked off beneath it.
    /// </summary>
    /// <remarks>
    /// Completing a subtask pays nothing at all (DEC-014), and "nothing" has to include the
    /// contract. A discharge driven by "a completion happened" rather than by the gate's own
    /// recorded answer would unlock the parent's fight for a piece of work worth no XP.
    /// </remarks>
    [Fact]
    public async Task Completing_a_subtask_discharges_nothing()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var parent = await AddTaskAsync(harness, daysOverdue: 12, tags: ["Work"]);
        var child = await AddTaskAsync(harness, "A piece of it", parentId: parent.Id);

        var contract = await AcceptAsync(harness, parent.Id);

        var completed = await harness.Gamification.CompleteAsync(harness.UserId, child.Id, 0, default);

        Assert.NotNull(completed);
        Assert.Equal(0, completed.XpGained);

        Assert.Null(await harness.Hunts.DischargeAsync(harness.UserId, child.Id, default));
        Assert.Null(await harness.Hunts.DischargeAsync(harness.UserId, parent.Id, default));

        Assert.Equal(HuntContractStatus.Accepted, (await ContractAsync(harness, contract.Id)).Status);

        var refused = await harness.Hunts.FightAsync(harness.UserId, contract.Id, default);

        Assert.False(refused.Ok);
        Assert.Equal(RpgFailure.HuntNotDischarged, refused.Failure);
    }

    /// <summary>
    /// The contract in hand is discharged by its own task and by no other.
    /// </summary>
    /// <remarks>
    /// The cheapest farm on the board if it were wrong: take the contract on the worst task on
    /// the list, then tick off something trivial to collect. The discharge is keyed off the
    /// contract's own TaskId rather than off "a completion happened", which is what closes it.
    /// </remarks>
    [Fact]
    public async Task Completing_a_different_task_never_discharges_the_contract_in_hand()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var hunted = await AddTaskAsync(harness, "The worst one", Difficulty.Epic, 200, ["work"]);
        var trivial = await AddTaskAsync(harness, "Something easy", Difficulty.Easy);

        var contract = await AcceptAsync(harness, hunted.Id);

        await harness.Gamification.CompleteAsync(harness.UserId, trivial.Id, 0, default);

        Assert.Null(await harness.Hunts.DischargeAsync(harness.UserId, trivial.Id, default));
        Assert.Equal(HuntContractStatus.Accepted, (await ContractAsync(harness, contract.Id)).Status);

        // And doing the real work does discharge it, so the refusal above was about which task
        // and not about the contract being undischargeable.
        await harness.Gamification.CompleteAsync(harness.UserId, hunted.Id, 0, default);

        Assert.NotNull(await harness.Hunts.DischargeAsync(harness.UserId, hunted.Id, default));
        Assert.Equal(HuntContractStatus.Discharged, (await ContractAsync(harness, contract.Id)).Status);
    }

    /// <summary>
    /// A recurring task carries one contract a period, and a repeat inside it pays nothing.
    /// </summary>
    /// <remarks>
    /// The second side door DEC-014 names. Without the gate a daily task is a contract printer:
    /// take one, discharge it, press Done again, take another, forever, on a task that is worth
    /// XP once a day.
    /// </remarks>
    [Fact]
    public async Task A_recurring_task_carries_one_contract_a_period()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var task = await AddTaskAsync(
            harness, "Water the plants", Difficulty.Medium, daysOverdue: 4,
            tags: ["work"], recurrence: RecurrenceRule.Daily);

        var contract = await AcceptAsync(harness, task.Id);

        await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);
        Assert.NotNull(await harness.Hunts.DischargeAsync(harness.UserId, task.Id, default));

        var fight = await harness.Hunts.FightAsync(harness.UserId, contract.Id, default);

        Assert.True(fight.Ok);
        await WinTheFightAsync(harness, fight.Value!.Encounter.Id);

        Assert.Equal(1, await StandingAsync(harness, FactionCatalog.TheLedger));

        var paid = await CharacterAsync(harness);

        // Now the repeat, inside the same period. It pays no XP, and it must pay no contract.
        var repeat = await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);

        Assert.NotNull(repeat);
        Assert.Equal(0, repeat.XpGained);
        Assert.Null(await harness.Hunts.DischargeAsync(harness.UserId, task.Id, default));

        var after = await CharacterAsync(harness);

        Assert.Equal(paid.Gold, after.Gold);
        Assert.Equal(paid.TotalXp, after.TotalXp);
        Assert.Equal(1, await StandingAsync(harness, FactionCatalog.TheLedger));

        // And no second contract this period, from either the board or the route.
        var again = await harness.Hunts.AcceptAsync(harness.UserId, task.Id, default);

        Assert.False(again.Ok);
        Assert.Equal(RpgFailure.NotHuntable, again.Failure);

        var board = await harness.Hunts.BoardAsync(harness.UserId, default);

        Assert.DoesNotContain(board.Offers, offer => offer.Task.Id == task.Id);
    }

    /// <summary>
    /// A completion that paid nothing discharges nothing, whatever put the row in that state.
    /// </summary>
    /// <remarks>
    /// The second of the three facts a discharge asks is <c>XpAwarded</c>, which is the answer
    /// <c>MayAwardAt</c> gave when the completion asked it, snapshotted on the row. This arranges
    /// the row directly because the reachable ways into that state each also fail an earlier
    /// check, and none of them would prove that this snapshot is load-bearing on its own. What is
    /// arranged is exactly what a non-paying completion leaves behind: Completed, both snapshots
    /// at zero, and a completion stamp that postdates the contract so the third fact cannot be
    /// what refuses it.
    /// </remarks>
    [Fact]
    public async Task A_completion_that_paid_nothing_discharges_nothing()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var task = await AddTaskAsync(harness, daysOverdue: 9, tags: ["work"]);
        var contract = await AcceptAsync(harness, task.Id);

        var live = await harness.Db.Tasks.SingleAsync(t => t.Id == task.Id);

        live.Status = TaskProgress.Completed;
        live.CompletedAt = DateTimeOffset.UtcNow;
        live.XpAwarded = 0;
        live.StaminaAwarded = 0;
        await harness.Db.SaveChangesAsync();

        Assert.True(live.CompletedAt >= contract.AcceptedAt);

        var before = await CharacterAsync(harness);

        Assert.Null(await harness.Hunts.DischargeAsync(harness.UserId, task.Id, default));

        var refused = await harness.Hunts.FightAsync(harness.UserId, contract.Id, default);

        Assert.False(refused.Ok);
        Assert.Equal(RpgFailure.HuntNotDischarged, refused.Failure);

        var after = await CharacterAsync(harness);

        Assert.Equal(before.Gold, after.Gold);
        Assert.Equal(HuntContractStatus.Accepted, (await ContractAsync(harness, contract.Id)).Status);
        Assert.Equal(0, await StandingAsync(harness, FactionCatalog.TheLedger));
    }

    /// <summary>A finished task has nothing left to promise, so it cannot be written up after the fact.</summary>
    /// <remarks>
    /// Without this a non-recurring task, which never sets XpEligibleFrom and so passes
    /// <c>MayAwardAt</c> forever, could be completed for its XP and written up as a contract
    /// afterwards. The completion that already happened could not discharge it, because it
    /// predates the acceptance, but the contract would sit on the board forever with no work left
    /// that could ever pay it.
    /// </remarks>
    [Fact]
    public async Task A_finished_task_can_never_be_written_up_after_the_fact()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var task = await AddTaskAsync(harness, daysOverdue: 20, tags: ["work"]);

        await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);

        var refused = await harness.Hunts.AcceptAsync(harness.UserId, task.Id, default);

        Assert.False(refused.Ok);
        Assert.Equal(RpgFailure.NotHuntable, refused.Failure);
        Assert.False(await harness.Db.HuntContracts.AnyAsync(c => c.UserId == harness.UserId));

        var board = await harness.Hunts.BoardAsync(harness.UserId, default);

        Assert.DoesNotContain(board.Offers, offer => offer.Task.Id == task.Id);
    }

    // -------------------------------------------------------------------------
    // Derived, not stored.
    // -------------------------------------------------------------------------

    /// <summary>
    /// The block is recomputed from the frozen facts on every read, and nothing else moves it.
    /// </summary>
    /// <remarks>
    /// Two halves, and both are needed. The first says a contract does not change shape when the
    /// hunter re-equips, levels up, or edits, retags and re-dates the task underneath it: the
    /// player took it on the strength of a purse and a stat block, and both have to hold.
    /// <para>
    /// The second says the block is genuinely derived rather than copied into columns nobody looks
    /// at: moving one frozen input on the row moves the block by exactly the arithmetic the
    /// archetype declares, which a stored copy could not do.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_contract_is_written_from_frozen_facts_and_derived_on_every_read()
    {
        var harness = await ArrangeAsync(AlwaysHits(), level: 3);

        var task = await AddTaskAsync(
            harness, difficulty: Difficulty.Hard, daysOverdue: 10, tags: ["work"]);

        await AddTaskAsync(harness, "One", parentId: task.Id);
        await AddTaskAsync(harness, "Two", parentId: task.Id);
        await AddTaskAsync(harness, "Three", parentId: task.Id);

        var contract = await AcceptAsync(harness, task.Id);
        var written = contract.Monster!;

        // Three subtasks makes it a Tangle, and ten days is short of the promotion.
        Assert.Equal(HuntArchetypeCatalog.Tangle, contract.ArchetypeKey);
        Assert.Equal(3, contract.Subtasks);
        Assert.Equal(10, contract.DaysOverdue);
        Assert.Equal(FactionCatalog.TheLedger, contract.FactionKey);

        // Everything the block could plausibly have been read off, moved underneath it.
        var sword = await harness.Loot.GrantAsync(
            harness.UserId, ItemCatalog.SilveredBlade, Rarity.Legendary, default);

        await harness.Adventurer.EquipAsync(harness.UserId, sword.Id, default);
        await ReachLevelAsync(harness, 6);

        var live = await harness.Db.Tasks.SingleAsync(t => t.Id == task.Id);

        live.Difficulty = Difficulty.Easy;
        live.Tags = ["home"];
        live.DueDate = DateTimeOffset.UtcNow.AddYears(1);
        live.Title = "Something else entirely";

        await AddTaskAsync(harness, "Four", parentId: task.Id);
        await AddTaskAsync(harness, "Five", parentId: task.Id);
        await harness.Db.SaveChangesAsync();

        await using (var fresh = postgres.CreateContext())
        {
            var reread = (await fresh.HuntContracts.AsNoTracking()
                .SingleAsync(c => c.Id == contract.Id)).Monster!;

            Assert.Equal(written.Key, reread.Key);
            Assert.Equal(written.Name, reread.Name);
            Assert.Equal(written.Level, reread.Level);
            Assert.Equal(written.MaxHitPoints, reread.MaxHitPoints);
            Assert.Equal(written.ArmourClass, reread.ArmourClass);
            Assert.Equal(written.AttackBonus, reread.AttackBonus);
            Assert.Equal(written.DamageNotation, reread.DamageNotation);
            Assert.Equal(written.MinGold, reread.MinGold);
            Assert.Equal(written.MaxGold, reread.MaxGold);
            Assert.Equal(written.DropChance, reread.DropChance);
        }

        // Now the other half. Move a frozen input and the block moves by exactly the archetype's
        // own arithmetic, which is only possible if it is being recomputed rather than read back.
        var tangle = HuntArchetypeCatalog.Find(HuntArchetypeCatalog.Tangle)!;
        var row = await harness.Db.HuntContracts.SingleAsync(c => c.Id == contract.Id);

        row.Subtasks = 5;
        await harness.Db.SaveChangesAsync();

        await using (var fresh = postgres.CreateContext())
        {
            var moved = (await fresh.HuntContracts.AsNoTracking()
                .SingleAsync(c => c.Id == contract.Id)).Monster!;

            // Two more counted subtasks, and the bulk they buy is the archetype's own number.
            // Kept well under the cap, which would otherwise absorb the difference and let a
            // stored copy pass this.
            Assert.Equal(
                written.MaxHitPoints + (2 * tangle.HitPointsPerSubtask), moved.MaxHitPoints);

            Assert.True(
                moved.MaxHitPoints
                    < HuntLadder.At(moved.Level).HitPoints * HuntRules.HitPointsCapMultiple,
                "The cap absorbed the change, so this proves nothing.");
        }
    }

    /// <summary>
    /// No column anywhere holds a number the stat block already answers (DEC-002).
    /// </summary>
    /// <remarks>
    /// The frozen inputs are historical facts about what was written down and belong on the row.
    /// Everything derived from them does not, and a copy would be the thing that drifts: the
    /// health bar, the phase thresholds and the purse are all read through the derivation, and a
    /// second stored answer would only be discovered by somebody comparing two screens.
    /// </remarks>
    [Fact]
    public async Task A_contract_stores_the_facts_it_froze_and_none_of_its_arithmetic()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var stored = harness.Db.Model
            .FindEntityType(typeof(HuntContract))!
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        Assert.Equal(
            [
                "AcceptedAt", "ArchetypeKey", "ClosedAt", "DaysOverdue", "DischargedAt",
                "EncounterId", "FactionKey", "Id", "Level", "Status", "Subtasks", "TaskId",
                "TaskTitle", "UserId"
            ],
            stored.Order(StringComparer.Ordinal));

        foreach (var derived in (string[])
                 [
                     "MonsterName", "MaxHitPoints", "ArmourClass", "AttackBonus", "DamageNotation",
                     "MinGold", "MaxGold", "DropChance", "BountyPercent", "Standing", "RewardFloor",
                     "StaminaCost", "Monster", "IsLive", "MayBeFought"
                 ])
        {
            Assert.DoesNotContain(derived, stored);
        }

        // The tasks table gains nothing at all, which is what keeps TaskDto, its mirrors and
        // ReopenAsync out of this phase entirely.
        Assert.DoesNotContain(
            harness.Db.Model.FindEntityType(typeof(TodoTask))!.GetProperties().Select(p => p.Name),
            name => name.Contains("Hunt", StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------------------------------------------
    // DEC-012 and DEC-003: gold and loot, never the one number that compounds.
    // -------------------------------------------------------------------------

    /// <summary>Winning a contract pays gold, loot and standing, and never experience.</summary>
    [Fact]
    public async Task Winning_a_contract_pays_gold_and_never_experience()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var task = await AddTaskAsync(harness, daysOverdue: 15, tags: ["work"]);
        var fight = await RunTheWholeWayAsync(harness, task.Id);

        // Measured after the completion and after the stamina, so the XP the task paid and the
        // stamina the fight cost are not confused with anything the win paid.
        var before = await CharacterAsync(harness);

        await WinTheFightAsync(harness, fight.Encounter.Id);

        var after = await CharacterAsync(harness);

        Assert.True(after.Gold > before.Gold);
        Assert.Equal(before.TotalXp, after.TotalXp);
        Assert.Equal(before.TasksCompleted, after.TasksCompleted);
        Assert.Equal(before.Stamina, after.Stamina);
        Assert.Equal(LevelCurve.LevelForXp(before.TotalXp), LevelCurve.LevelForXp(after.TotalXp));
        Assert.Equal(1, await StandingAsync(harness, FactionCatalog.TheLedger));
    }

    /// <summary>
    /// A contract's fight is one fight and is priced as one, on the same line as a tavern fight.
    /// </summary>
    /// <remarks>
    /// One stamina, charged where every fight is charged, and charged at the fight rather than at
    /// the promise. A player with an empty tank can still write up their whole backlog; what they
    /// cannot do is collect on it, and finishing something is what refills the tank (DEC-003).
    /// </remarks>
    [Fact]
    public async Task A_contract_costs_one_stamina_at_the_fight_and_nowhere_else()
    {
        var harness = await ArrangeAsync(AlwaysHits(), stamina: 2);

        var task = await AddTaskAsync(harness, daysOverdue: 30, tags: ["work"]);

        var before = await CharacterAsync(harness);
        var contract = await AcceptAsync(harness, task.Id);

        Assert.Equal(before.Stamina, (await CharacterAsync(harness)).Stamina);

        await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);
        Assert.NotNull(await harness.Hunts.DischargeAsync(harness.UserId, task.Id, default));

        var earned = await CharacterAsync(harness);

        Assert.True((await harness.Hunts.FightAsync(harness.UserId, contract.Id, default)).Ok);
        Assert.Equal(
            earned.Stamina - CombatService.StaminaPerEncounter,
            (await CharacterAsync(harness)).Stamina);

        // With nothing in the tank, the fight is refused before anything is written, however
        // overdue the task is. A backlog is never a way around the gate.
        var next = await AddTaskAsync(harness, "Another", daysOverdue: 300, tags: ["work"]);
        var second = await AcceptAsync(harness, next.Id);

        await harness.Gamification.CompleteAsync(harness.UserId, next.Id, 0, default);
        Assert.NotNull(await harness.Hunts.DischargeAsync(harness.UserId, next.Id, default));

        var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);

        character.Stamina = 0;
        await harness.Db.SaveChangesAsync();

        // The live fight from above is in the way first, so it is walked away from.
        var live = await harness.Combat.ActiveAsync(harness.UserId, default);

        Assert.NotNull(live);
        Assert.True((await harness.Combat.FleeAsync(harness.UserId, live.Id, default)).Ok);

        var refused = await harness.Hunts.FightAsync(harness.UserId, second.Id, default);

        Assert.False(refused.Ok);
        Assert.Equal(RpgFailure.NotEnoughStamina, refused.Failure);
        Assert.Equal(HuntContractStatus.Discharged, (await ContractAsync(harness, second.Id)).Status);
        Assert.False(await harness.Db.Encounters.AnyAsync(e => e.TaskId == next.Id));
    }

    /// <summary>One fight at a time, whichever kind of fight asked first.</summary>
    /// <remarks>
    /// Only the fight is governed by it. Taking a contract writes no encounter, so a brawl
    /// standing in the tavern cannot stop the player writing up their backlog.
    /// </remarks>
    [Fact]
    public async Task A_contract_cannot_be_fought_while_another_fight_is_live()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var task = await AddTaskAsync(harness, daysOverdue: 3, tags: ["work"]);
        var contract = await AcceptAsync(harness, task.Id);

        await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);
        Assert.NotNull(await harness.Hunts.DischargeAsync(harness.UserId, task.Id, default));

        Assert.True((await harness.Combat.StartAsync(
            harness.UserId, MonsterCatalog.GiantRat, default)).Ok);

        var refused = await harness.Hunts.FightAsync(harness.UserId, contract.Id, default);

        Assert.False(refused.Ok);

        // Through the existing path rather than a new failure member, so the client's handling of
        // "you are already in a fight" needs no change.
        Assert.Equal(RpgFailure.EncounterAlreadyActive, refused.Failure);
        Assert.False(await harness.Db.Encounters.AnyAsync(e => e.TaskId == task.Id));
        Assert.Equal(HuntContractStatus.Discharged, (await ContractAsync(harness, contract.Id)).Status);

        // The other direction, which is the same index refusing from the other screen.
        var tavern = await harness.Combat.ActiveAsync(harness.UserId, default);

        Assert.NotNull(tavern);
        Assert.True((await harness.Combat.FleeAsync(harness.UserId, tavern.Id, default)).Ok);
        Assert.True((await harness.Hunts.FightAsync(harness.UserId, contract.Id, default)).Ok);

        var second = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);

        Assert.False(second.Ok);
        Assert.Equal(RpgFailure.EncounterAlreadyActive, second.Failure);
    }

    /// <summary>A contract buys one fight, and the second attempt buys nothing.</summary>
    /// <remarks>
    /// The contract closes as the encounter is written, in the same save, so there is no window
    /// in which a discharged contract sits beside a live fight it could buy a second time. Two
    /// stamina for two bounties on one piece of work is the shape this refuses.
    /// </remarks>
    [Fact]
    public async Task A_discharged_contract_buys_exactly_one_fight()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var task = await AddTaskAsync(harness, daysOverdue: 20, tags: ["work"]);
        var fight = await RunTheWholeWayAsync(harness, task.Id);

        var closed = await ContractAsync(harness, fight.Contract!.Id);

        Assert.Equal(HuntContractStatus.Fought, closed.Status);
        Assert.Equal(fight.Encounter.Id, closed.EncounterId);
        Assert.NotNull(closed.ClosedAt);

        await WinTheFightAsync(harness, fight.Encounter.Id);

        var paid = await CharacterAsync(harness);

        var again = await harness.Hunts.FightAsync(harness.UserId, closed.Id, default);

        Assert.False(again.Ok);
        Assert.Equal(RpgFailure.HuntAlreadyFought, again.Failure);

        var after = await CharacterAsync(harness);

        Assert.Equal(paid.Gold, after.Gold);
        Assert.Equal(paid.Stamina, after.Stamina);
        Assert.Single(await harness.Db.Encounters.Where(e => e.TaskId == task.Id).ToListAsync());
    }

    // -------------------------------------------------------------------------
    // Reopening, editing, deleting, and the one-contract-per-window rule.
    // -------------------------------------------------------------------------

    /// <summary>
    /// A contract that has been collected on cannot be taken again on the same task.
    /// </summary>
    /// <remarks>
    /// The farm, asked directly. One contract per task per window, derived from the contracts on
    /// the table rather than a flag: a contract taken before the task's last completion no longer
    /// blocks, which is what lets a daily be written up again tomorrow, and one taken after it
    /// still does. A won contract is not a licence to write another.
    /// </remarks>
    [Fact]
    public async Task A_contract_collected_on_cannot_be_taken_again_on_the_same_task()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var task = await AddTaskAsync(harness, daysOverdue: 7, tags: ["work"]);
        var fight = await RunTheWholeWayAsync(harness, task.Id);

        await WinTheFightAsync(harness, fight.Encounter.Id);

        // The task is done, so this is refused for being finished rather than for being taken.
        var whileDone = await harness.Hunts.AcceptAsync(harness.UserId, task.Id, default);

        Assert.False(whileDone.Ok);
        Assert.Equal(RpgFailure.NotHuntable, whileDone.Failure);

        // Reopened, the task is open again, and now the old contract is what refuses it. This is
        // the complete-reopen-rewrite farm, closed by a derived comparison rather than a column.
        await harness.Gamification.ReopenAsync(harness.UserId, task.Id, default);

        var reopened = await harness.Hunts.AcceptAsync(harness.UserId, task.Id, default);

        Assert.False(reopened.Ok);
        Assert.Equal(RpgFailure.HuntAlreadyTaken, reopened.Failure);

        var board = await harness.Hunts.BoardAsync(harness.UserId, default);

        Assert.DoesNotContain(board.Offers, offer => offer.Task.Id == task.Id);
        Assert.Single(await harness.Db.HuntContracts.Where(c => c.TaskId == task.Id).ToListAsync());

        // Walking away from the fight does not reopen the window either. A contract taken is a
        // contract taken.
        var fled = await AddTaskAsync(harness, "Walked away from", daysOverdue: 7);
        var walked = await RunTheWholeWayAsync(harness, fled.Id);

        await harness.Combat.FleeAsync(harness.UserId, walked.Encounter.Id, default);
        await harness.Gamification.ReopenAsync(harness.UserId, fled.Id, default);

        var retaken = await harness.Hunts.AcceptAsync(harness.UserId, fled.Id, default);

        Assert.False(retaken.Ok);
        Assert.Equal(RpgFailure.HuntAlreadyTaken, retaken.Failure);

        // And a contract walked away from is worth no standing, because standing counts wins.
        Assert.Equal(1, await StandingAsync(harness, FactionCatalog.TheLedger));
    }

    /// <summary>
    /// Reopening takes back a discharge, and leaves a fight that already happened alone.
    /// </summary>
    /// <remarks>
    /// A discharged contract is the record of work that was done. Reopening says it was not, so
    /// the contract goes back to waiting: without this, "finish it, undo it, collect the bounty"
    /// would pay gold, loot and standing on a task that ends the sequence unfinished, which is the
    /// shape DEC-013 exists to refuse. Nothing is taken from the player by it, because a contract
    /// discharges again the moment the task is genuinely finished.
    /// <para>
    /// A contract already fought is untouched, matching how a badge, a quest advance and spent
    /// stamina all survive a reopen: that fight happened, and the chronicle does not un-happen.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Reopening_takes_back_a_discharge_and_leaves_a_fought_contract_alone()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var undone = await AddTaskAsync(harness, "Finished, then not", daysOverdue: 18, tags: ["work"]);
        var contract = await AcceptAsync(harness, undone.Id);

        await harness.Gamification.CompleteAsync(harness.UserId, undone.Id, 0, default);
        Assert.NotNull(await harness.Hunts.DischargeAsync(harness.UserId, undone.Id, default));

        await harness.Gamification.ReopenAsync(harness.UserId, undone.Id, default);
        Assert.NotNull(await harness.Hunts.UndischargeAsync(harness.UserId, undone.Id, default));

        var waiting = await ContractAsync(harness, contract.Id);

        Assert.Equal(HuntContractStatus.Accepted, waiting.Status);
        Assert.Null(waiting.DischargedAt);

        var refused = await harness.Hunts.FightAsync(harness.UserId, contract.Id, default);

        Assert.False(refused.Ok);
        Assert.Equal(RpgFailure.HuntNotDischarged, refused.Failure);

        // Doing the work again earns it back, so nothing was confiscated.
        await harness.Gamification.CompleteAsync(harness.UserId, undone.Id, 0, default);
        Assert.NotNull(await harness.Hunts.DischargeAsync(harness.UserId, undone.Id, default));
        Assert.True((await harness.Hunts.FightAsync(harness.UserId, contract.Id, default)).Ok);

        // The other half: a fight that already happened survives a reopen whole, gold, standing
        // and all, exactly as an unlocked badge does.
        var won = await AddTaskAsync(harness, "Fought and finished", daysOverdue: 18, tags: ["work"]);

        var live = await harness.Combat.ActiveAsync(harness.UserId, default);

        Assert.NotNull(live);
        await harness.Combat.FleeAsync(harness.UserId, live.Id, default);

        var fight = await RunTheWholeWayAsync(harness, won.Id);

        await WinTheFightAsync(harness, fight.Encounter.Id);

        var paid = await CharacterAsync(harness);
        var standing = await StandingAsync(harness, FactionCatalog.TheLedger);

        var reopened = await harness.Gamification.ReopenAsync(harness.UserId, won.Id, default);

        Assert.NotNull(reopened);
        Assert.Equal(Difficulty.Hard.BaseXp(), reopened.XpLost);
        Assert.Null(await harness.Hunts.UndischargeAsync(harness.UserId, won.Id, default));

        var after = await CharacterAsync(harness);

        Assert.Equal(paid.TotalXp - Difficulty.Hard.BaseXp(), after.TotalXp);
        Assert.Equal(paid.Gold, after.Gold);
        Assert.Equal(standing, await StandingAsync(harness, FactionCatalog.TheLedger));
        Assert.Equal(
            HuntContractStatus.Fought, (await ContractAsync(harness, fight.Contract!.Id)).Status);
    }

    /// <summary>
    /// Editing, retagging or re-dating a task never moves the contract written on it.
    /// </summary>
    /// <remarks>
    /// The whole point of freezing the facts at acceptance. Without it a player could retag a
    /// task one keystroke before collecting and redirect the reward to whichever banner is holding
    /// the item they want, or re-date it and widen the purse. The way to have a contract re-priced
    /// is to tear it up and take it again, which is free and which re-reads the task from scratch.
    /// </remarks>
    [Fact]
    public async Task Editing_a_task_never_moves_the_contract_written_on_it()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var task = await AddTaskAsync(harness, "As written", Difficulty.Hard, 10, ["work"]);
        var contract = await AcceptAsync(harness, task.Id);

        var live = await harness.Db.Tasks.SingleAsync(t => t.Id == task.Id);

        live.Title = "Renamed";
        live.Difficulty = Difficulty.Epic;
        live.Tags = ["health"];
        live.DueDate = DateTimeOffset.UtcNow.AddDays(-400);
        live.UpdatedAt = DateTimeOffset.UtcNow;
        await harness.Db.SaveChangesAsync();

        var reread = await ContractAsync(harness, contract.Id);

        Assert.Equal("As written", reread.TaskTitle);
        Assert.Equal(FactionCatalog.TheLedger, reread.FactionKey);
        Assert.Equal(10, reread.DaysOverdue);
        Assert.Equal(contract.ArchetypeKey, reread.ArchetypeKey);
        Assert.Equal(contract.Level, reread.Level);

        // And the fight it opens is opened against exactly that, not against the edit.
        await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);
        Assert.NotNull(await harness.Hunts.DischargeAsync(harness.UserId, task.Id, default));

        var fight = await harness.Hunts.FightAsync(harness.UserId, contract.Id, default);

        Assert.True(fight.Ok);
        Assert.Equal(10, fight.Value!.Encounter.HuntDaysOverdue);
        Assert.Equal(FactionCatalog.TheLedger, fight.Value.Encounter.HuntFactionKey);
        Assert.Equal(contract.ArchetypeKey, fight.Value.Encounter.MonsterKey);
    }

    /// <summary>
    /// Tearing up a contract is free, and cannot be turned into a gain.
    /// </summary>
    /// <remarks>
    /// The way out, and the only way to have a contract re-priced after the task under it has
    /// genuinely changed shape. It cannot be farmed: a fresh contract can only be discharged by a
    /// completion that postdates it, so tearing up a discharged one forfeits the fight rather than
    /// banking it, and the work has to be done again before anything can be collected.
    /// </remarks>
    [Fact]
    public async Task Tearing_up_a_contract_is_free_and_buys_nothing()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var task = await AddTaskAsync(harness, daysOverdue: 12, tags: ["work"]);
        var first = await AcceptAsync(harness, task.Id);

        var before = await CharacterAsync(harness);

        var torn = await harness.Hunts.AbandonAsync(harness.UserId, first.Id, default);

        Assert.True(torn.Ok);
        Assert.Equal(HuntContractStatus.Abandoned, (await ContractAsync(harness, first.Id)).Status);

        var after = await CharacterAsync(harness);

        Assert.Equal(before.Stamina, after.Stamina);
        Assert.Equal(before.Gold, after.Gold);
        Assert.Equal(before.TotalXp, after.TotalXp);

        // The task is back on the board, priced again from scratch.
        var board = await harness.Hunts.BoardAsync(harness.UserId, default);

        Assert.Contains(board.Offers, offer => offer.Task.Id == task.Id);
        Assert.DoesNotContain(board.Contracts, view => view.Contract.Id == first.Id);

        var second = await AcceptAsync(harness, task.Id);

        Assert.NotEqual(first.Id, second.Id);

        // Discharging a torn up contract and collecting on it are both refused, so tearing one
        // up cannot be a way to hold two.
        Assert.False((await harness.Hunts.AbandonAsync(harness.UserId, first.Id, default)).Ok);
        Assert.Equal(
            RpgFailure.HuntAlreadyFought,
            (await harness.Hunts.FightAsync(harness.UserId, first.Id, default)).Failure);

        await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);

        var discharged = await harness.Hunts.DischargeAsync(harness.UserId, task.Id, default);

        Assert.NotNull(discharged);
        Assert.Equal(second.Id, discharged.Contract.Id);
        Assert.Equal(HuntContractStatus.Abandoned, (await ContractAsync(harness, first.Id)).Status);
    }

    /// <summary>
    /// A task tidied away tears up the promise and leaves the earned fight standing.
    /// </summary>
    /// <remarks>
    /// Two states, two answers, and a referential action cannot tell them apart, which is why the
    /// endpoint sweeps the waiting ones itself before the delete. An accepted contract is a
    /// promise to finish this task, and deleting the task deletes the only thing that could ever
    /// discharge it; nothing was spent on it, so nothing is taken. A discharged one keeps its
    /// fight, because the work was done and tidying the row away afterwards must not take back
    /// what doing it earned.
    /// </remarks>
    [Fact]
    public async Task Deleting_a_task_tears_up_a_waiting_contract_and_keeps_an_earned_one()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var waiting = await AddTaskAsync(harness, "Never finished", daysOverdue: 45, tags: ["work"]);
        var earned = await AddTaskAsync(harness, "Finished first", daysOverdue: 45, tags: ["work"]);

        var promise = await AcceptAsync(harness, waiting.Id);
        var owed = await AcceptAsync(harness, earned.Id);

        await harness.Gamification.CompleteAsync(harness.UserId, earned.Id, 0, default);
        Assert.NotNull(await harness.Hunts.DischargeAsync(harness.UserId, earned.Id, default));

        // Exactly what DeleteTask does, in the order it does it.
        await harness.Db.HuntContracts
            .Where(c => c.TaskId == waiting.Id && c.Status == HuntContractStatus.Accepted)
            .ExecuteUpdateAsync(update => update
                .SetProperty(c => c.Status, HuntContractStatus.Abandoned)
                .SetProperty(c => c.ClosedAt, DateTimeOffset.UtcNow));

        await harness.Db.Tasks.Where(t => t.Id == waiting.Id).ExecuteDeleteAsync();
        await harness.Db.Tasks.Where(t => t.Id == earned.Id).ExecuteDeleteAsync();

        // A fresh context, because ExecuteDelete bypasses the change tracker and the one above is
        // still holding contracts that remember a task id the database has already nulled. A real
        // request arrives on a new scope with a new context, and so does this.
        await using var fresh = postgres.CreateContext();

        var tornUp = await fresh.HuntContracts.AsNoTracking().SingleAsync(c => c.Id == promise.Id);

        Assert.Equal(HuntContractStatus.Abandoned, tornUp.Status);
        Assert.Null(tornUp.TaskId);

        var survivor = await fresh.HuntContracts.AsNoTracking().SingleAsync(c => c.Id == owed.Id);

        Assert.Equal(HuntContractStatus.Discharged, survivor.Status);
        Assert.Null(survivor.TaskId);

        // Still worth exactly what it was written for, and still collectable: every number it
        // was worth is on the contract and not on the task.
        Assert.Equal(45, survivor.DaysOverdue);
        Assert.Equal(FactionCatalog.TheLedger, survivor.FactionKey);
        Assert.Equal("Finished first", survivor.TaskTitle);
        Assert.NotNull(survivor.Monster);

        var sheets = new CharacterSheetService(fresh);
        var loot = new LootService(fresh, AlwaysHits());
        var quests = new QuestService(fresh, loot);
        var combat = new CombatService(fresh, AlwaysHits(), sheets, loot, quests);
        var hunts = new HuntService(fresh, sheets, combat);

        var fight = await hunts.FightAsync(harness.UserId, owed.Id, default);

        Assert.True(fight.Ok, fight.Message);
        Assert.Null(fight.Value!.Encounter.TaskId);
        Assert.True(fight.Value.Encounter.IsHunt);
        Assert.True((await combat.AttackAsync(harness.UserId, fight.Value.Encounter.Id, default)).Ok);
    }

    // -------------------------------------------------------------------------
    // Factions and the board.
    // -------------------------------------------------------------------------

    /// <summary>
    /// However the tag was typed, the wins land under one banner.
    /// </summary>
    /// <remarks>
    /// NormalizeTags dedupes case-insensitively within one task only, so "Work", "work" and
    /// "WORK" all genuinely exist across a list. Matching them the way the endpoint's own tag
    /// filter matches, byte-exactly in SQL, would split one banner into three and nobody would
    /// ever reach Trusted.
    /// </remarks>
    [Fact]
    public async Task Three_spellings_of_one_tag_are_one_banner()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        foreach (var typed in (string[])["Work", "work", "WORK"])
        {
            var task = await AddTaskAsync(harness, $"Tagged {typed}", daysOverdue: 8, tags: [typed]);
            var fight = await RunTheWholeWayAsync(harness, task.Id);

            // The catalog key, never the tag, so no user casing reaches the column.
            Assert.Equal(FactionCatalog.TheLedger, fight.Encounter.HuntFactionKey);

            await WinTheFightAsync(harness, fight.Encounter.Id);
        }

        Assert.Equal(3, await StandingAsync(harness, FactionCatalog.TheLedger));

        var board = await harness.Hunts.BoardAsync(harness.UserId, default);
        var ledger = Assert.Single(board.Factions, f => f.Faction.Key == FactionCatalog.TheLedger);

        Assert.Equal(3, ledger.WonHunts);
        Assert.Equal(FactionStanding.Noticed, ledger.Standing);

        // Standing is per banner, so nothing leaked sideways into the other four.
        Assert.All(
            board.Factions.Where(f => f.Faction.Key != FactionCatalog.TheLedger),
            f =>
            {
                Assert.Equal(0, f.WonHunts);
                Assert.Equal(FactionStanding.Unknown, f.Standing);
            });
    }

    /// <summary>
    /// The guaranteed reward is a bounty's reward, and only under a banner.
    /// </summary>
    /// <remarks>
    /// A contract taken on a task that is not late pays a monster's ordinary gold and nothing
    /// else, which keeps a fresh contract strictly worse per stamina than a band-appropriate
    /// tavern fight and puts the treasure in the backlog where DEC-013 says it belongs.
    /// </remarks>
    [Fact]
    public async Task A_contract_reward_is_paid_only_by_a_backlog_under_a_banner()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        Assert.Null(await CollectOneAsync(harness, "On time, under a banner", 0, ["work"]));
        Assert.Null(await CollectOneAsync(harness, "Late, under no banner", 20, []));

        var earned = await CollectOneAsync(harness, "Late, under a banner", 20, ["work"]);

        Assert.NotNull(earned);

        // Out of the banner's own table, never a monster's and never the archetype's.
        Assert.Contains(
            FactionCatalog.Find(FactionCatalog.TheLedger)!.RewardTable,
            entry => entry.ItemKey == earned.ItemKey);

        return;

        static async Task<InventoryItem?> CollectOneAsync(
            Harness harness, string title, int daysOverdue, string[] tags)
        {
            var task = await AddTaskAsync(harness, title, daysOverdue: daysOverdue, tags: tags);
            var fight = await RunTheWholeWayAsync(harness, task.Id);

            // Paid on the round that ends it, beside the monster's own drop.
            return (await WinTheFightAsync(harness, fight.Encounter.Id)).ClearReward;
        }
    }

    /// <summary>
    /// The board quotes exactly the fight it sells, and offers every task that could carry one.
    /// </summary>
    /// <remarks>
    /// Two properties in one place because they are the same property read twice. The board is
    /// priced through the same derivation the fight will use, so it cannot advertise a purse the
    /// win does not pay; and it is not trimmed on the server, because the same list is what a task
    /// card asks whether its own task carries a contract. Trimmed to twenty rows, a player with
    /// twenty-one overdue tasks lost the seal, the button and every route to a contract on the
    /// ones that fell off, which is precisely backwards: the bigger the backlog, the more of it
    /// silently stopped being worth anything.
    /// </remarks>
    [Fact]
    public async Task The_board_quotes_the_fight_it_sells_and_offers_every_huntable_task()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var task = await AddTaskAsync(
            harness, "Ancient thing", Difficulty.Epic, daysOverdue: 40, tags: ["Work"]);

        await AddTaskAsync(harness, "Bit of it", parentId: task.Id);

        var fresh = await AddTaskAsync(harness, "Only just due", Difficulty.Easy);

        // Well past any display cap, and every one of them genuinely huntable.
        for (var index = 0; index < 25; index++)
        {
            await AddTaskAsync(harness, $"Backlog {index}", daysOverdue: index + 1);
        }

        var board = await harness.Hunts.BoardAsync(harness.UserId, default);

        Assert.True(board.Offers.Count >= 27, $"Only {board.Offers.Count} offers came back.");

        // Worst first, because that is the one the game is trying to get finished.
        Assert.Equal(task.Id, board.Offers[0].Task.Id);
        Assert.Contains(board.Offers, offer => offer.Task.Id == fresh.Id);
        Assert.All(board.Offers, offer => Assert.True(offer.DaysOverdue >= 0));

        // The least overdue one is the one a trimmed board dropped, and it has to be answerable.
        Assert.Contains(board.Offers, offer => offer.Task.Title == "Backlog 0");

        var offered = board.Offers[0];

        // One subtask makes it a Tangle before age is considered, and a month promotes a Tangle
        // to a Hydra rather than to the Dread a subtask-free Epic would have become.
        Assert.Equal(HuntArchetypeCatalog.Hydra, offered.ArchetypeKey);
        Assert.Equal(FactionCatalog.TheLedger, offered.Faction!.Key);
        Assert.Equal(1, offered.Subtasks);

        // Reading the board rolls nothing and writes nothing, so it is free and repeatable.
        var before = await CharacterAsync(harness);
        var second = await harness.Hunts.BoardAsync(harness.UserId, default);
        var after = await CharacterAsync(harness);

        Assert.Equal(before.Stamina, after.Stamina);
        Assert.Equal(before.Gold, after.Gold);
        Assert.Equal(offered.Monster.MaxGold, second.Offers[0].Monster.MaxGold);
        Assert.False(await harness.Db.HuntContracts.AnyAsync(c => c.UserId == harness.UserId));

        var contract = await AcceptAsync(harness, task.Id);

        await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);
        Assert.NotNull(await harness.Hunts.DischargeAsync(harness.UserId, task.Id, default));

        var opened = await harness.Hunts.FightAsync(harness.UserId, contract.Id, default);

        Assert.True(opened.Ok);

        var fought = opened.Value!.Encounter.Monster!;

        Assert.Equal(offered.Monster.Key, fought.Key);
        Assert.Equal(offered.Monster.Name, fought.Name);
        Assert.Equal(offered.Monster.Level, fought.Level);
        Assert.Equal(offered.Monster.MaxHitPoints, fought.MaxHitPoints);
        Assert.Equal(offered.Monster.ArmourClass, fought.ArmourClass);
        Assert.Equal(offered.Monster.AttackBonus, fought.AttackBonus);
        Assert.Equal(offered.Monster.MinGold, fought.MinGold);
        Assert.Equal(offered.Monster.MaxGold, fought.MaxGold);
        Assert.Equal(offered.Monster.DropChance, fought.DropChance);

        // Taken, so it comes off the offers and onto the contracts rather than being offered twice.
        var afterwards = await harness.Hunts.BoardAsync(harness.UserId, default);

        Assert.DoesNotContain(afterwards.Offers, offer => offer.Task.Id == task.Id);
    }

    /// <summary>The board separates what could be taken from what has been.</summary>
    /// <remarks>
    /// The client renders the two lists differently and must not have to work out which is which:
    /// an accepted contract is offered no fight at all, and a discharged one is the only thing on
    /// the screen with a fight button on it.
    /// </remarks>
    [Fact]
    public async Task The_board_shows_what_is_promised_beside_what_is_owed()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var waiting = await AddTaskAsync(harness, "Still outstanding", daysOverdue: 5, tags: ["work"]);
        var owed = await AddTaskAsync(harness, "Done, not collected", daysOverdue: 25, tags: ["home"]);
        var free = await AddTaskAsync(harness, "Nobody has claimed it", daysOverdue: 3);

        await AcceptAsync(harness, waiting.Id);
        await AcceptAsync(harness, owed.Id);

        await harness.Gamification.CompleteAsync(harness.UserId, owed.Id, 0, default);
        Assert.NotNull(await harness.Hunts.DischargeAsync(harness.UserId, owed.Id, default));

        var board = await harness.Hunts.BoardAsync(harness.UserId, default);

        Assert.Contains(board.Offers, offer => offer.Task.Id == free.Id);
        Assert.DoesNotContain(board.Offers, offer => offer.Task.Id == waiting.Id);
        Assert.DoesNotContain(board.Offers, offer => offer.Task.Id == owed.Id);

        var promised = Assert.Single(board.Contracts, view => view.Contract.TaskId == waiting.Id);
        var earned = Assert.Single(board.Contracts, view => view.Contract.TaskId == owed.Id);

        Assert.False(promised.Contract.MayBeFought);
        Assert.True(earned.Contract.MayBeFought);

        // Both carry their whole block, because the card quotes what collecting would be worth.
        Assert.NotNull(promised.Monster);
        Assert.Equal(FactionCatalog.TheLedger, promised.Faction!.Key);
        Assert.Equal(FactionCatalog.TheHearth, earned.Faction!.Key);
    }

    /// <summary>
    /// A daily task kept faithfully is not a year overdue, whatever its due date still says.
    /// </summary>
    /// <remarks>
    /// The sharpest DEC-013 trap in the phase, and it points the wrong way: recurrence never
    /// advances DueDate, so <see cref="TodoTask.DaysOverdue"/> reports "water the plants", due a
    /// year ago and done every single day since, as 365 days overdue forever. Keyed off that, the
    /// best kept task on the list would draw the largest purse in the game and the promoted shape
    /// to go with it. A contract measures a recurring task from the gate that reopened it instead.
    /// </remarks>
    [Fact]
    public async Task A_daily_task_kept_faithfully_is_never_a_year_overdue()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var task = await AddTaskAsync(
            harness, "Water the plants", Difficulty.Medium, daysOverdue: 365,
            recurrence: RecurrenceRule.Daily);

        await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);

        // The successor is what is on the board now, and it is due tomorrow rather than a year
        // ago however far behind the original had fallen. Nothing is arranged by hand here,
        // which is the point: the completion itself moved the due date (DEC-015).
        var completed = await harness.Db.Tasks.SingleAsync(t => t.Id == task.Id);

        Assert.NotNull(completed.SpawnedTaskId);

        var live = await harness.Db.Tasks.SingleAsync(t => t.Id == completed.SpawnedTaskId);

        var now = DateTimeOffset.UtcNow;

        // What the model's own method says, quoted so the fix is on the record. It used to
        // report 365 here, forever, and HuntService carried a second calculation to dodge it.
        Assert.False(live.IsCompleted);
        Assert.Equal(0, live.DaysOverdue(now));
        Assert.True(live.DueDate > now);

        var board = await harness.Hunts.BoardAsync(harness.UserId, default);
        var offer = Assert.Single(board.Offers, o => o.Task.Id == live.Id);

        Assert.Equal(0, offer.DaysOverdue);
        Assert.Equal(BountyRules.BasePercent, BountyRules.BountyPercent(offer.DaysOverdue));
        Assert.Equal(HuntArchetypeCatalog.Drudge, offer.ArchetypeKey);
        Assert.Equal(HuntLadder.At(offer.Monster.Level).MinGold, offer.Monster.MinGold);

        var contract = await AcceptAsync(harness, live.Id);

        Assert.Equal(0, contract.DaysOverdue);

        // A recurring task that has genuinely never been done is still overdue by its due date,
        // so the measurement moved for the rolled over case only.
        var neglected = await AddTaskAsync(
            harness, "Never once watered", Difficulty.Medium, daysOverdue: 40,
            recurrence: RecurrenceRule.Weekly);

        var second = await harness.Hunts.BoardAsync(harness.UserId, default);

        Assert.Equal(40, Assert.Single(second.Offers, o => o.Task.Id == neglected.Id).DaysOverdue);
    }

    /// <summary>
    /// A contract never reaches the codex, by either of the two doors that write to it.
    /// </summary>
    /// <remarks>
    /// The codex is a record of the bestiary's kinds. An archetype key written into it would be a
    /// row MonsterCatalog cannot resolve: invisible on the screen, counted in nothing, and
    /// impossible to remove once written. The discovery quest objective rides the same call, so a
    /// contract that recorded a sighting would also pay a quest for meeting a monster that does
    /// not exist.
    /// </remarks>
    [Fact]
    public async Task A_contract_never_reaches_the_codex()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var fought = await AddTaskAsync(harness, "Fought to the end", daysOverdue: 3);
        var walked = await AddTaskAsync(harness, "Walked away from", daysOverdue: 3);

        var first = await RunTheWholeWayAsync(harness, fought.Id);

        await WinTheFightAsync(harness, first.Encounter.Id);

        var second = await RunTheWholeWayAsync(harness, walked.Id);

        await harness.Combat.FleeAsync(harness.UserId, second.Encounter.Id, default);

        var codex = await harness.Db.BestiaryEntries.AsNoTracking()
            .Where(e => e.UserId == harness.UserId)
            .ToListAsync();

        // Not a single archetype key, and in fact not a single row: both fights were contracts.
        Assert.DoesNotContain(codex, entry => HuntArchetypeCatalog.Exists(entry.MonsterKey));
        Assert.Empty(codex);
    }

    /// <summary>
    /// The chronicle can name the thing a hunter has fought most, even when it is an archetype.
    /// </summary>
    /// <remarks>
    /// Hunts are ordinary encounter rows and their MonsterKey is a HuntArchetypeCatalog key, which
    /// MonsterCatalog has never heard of. The summary resolved the modal key through MonsterCatalog
    /// alone, so it came back null with a real non-zero count beside it, and the client gates the
    /// whole "Most fought" line on the name being present. The line therefore vanished as soon as
    /// contracts outnumbered any single bestiary monster, which is the normal steady state: one
    /// archetype covers every task of that shape, while the bestiary has eleven kinds to spread
    /// across.
    /// </remarks>
    [Fact]
    public async Task The_chronicle_can_name_a_contract_as_the_thing_fought_most()
    {
        var harness = await ArrangeAsync(AlwaysHits(), stamina: 40);

        // Two contracts of the same shape against one tavern fight, so the archetype is the modal
        // key and the summary has to resolve it out of the other catalog.
        foreach (var index in (int[])[1, 2])
        {
            var task = await AddTaskAsync(harness, $"Chore {index}", Difficulty.Easy, daysOverdue: 1);
            var fight = await RunTheWholeWayAsync(harness, task.Id);

            await WinTheFightAsync(harness, fight.Encounter.Id);
        }

        Assert.True((await harness.Combat.StartAsync(
            harness.UserId, MonsterCatalog.GiantRat, default)).Ok);

        var rat = await harness.Combat.ActiveAsync(harness.UserId, default);

        Assert.NotNull(rat);
        await WinTheFightAsync(harness, rat.Id);

        var summary = await harness.Combat.SummaryAsync(harness.UserId, default);

        Assert.Equal(3, summary.Fought);
        Assert.Equal(2, summary.MostFoughtCount);

        // Named, not null, and named as the archetype rather than as a raw key.
        var drudge = HuntArchetypeCatalog.Find(HuntArchetypeCatalog.Drudge)!;

        Assert.Equal(drudge.Noun, summary.MostFoughtMonster);

        // The bestiary side still reads as it always did, so the fallback did not swallow it.
        var tavernOnly = await AddTaskAsync(harness, "Not fought", daysOverdue: 1);

        Assert.NotNull(tavernOnly);

        for (var index = 0; index < 2; index++)
        {
            Assert.True((await harness.Combat.StartAsync(
                harness.UserId, MonsterCatalog.GiantRat, default)).Ok);

            var again = await harness.Combat.ActiveAsync(harness.UserId, default);

            Assert.NotNull(again);
            await WinTheFightAsync(harness, again.Id);
        }

        var second = await harness.Combat.SummaryAsync(harness.UserId, default);

        Assert.Equal(MonsterCatalog.Find(MonsterCatalog.GiantRat)!.Name, second.MostFoughtMonster);
    }

    /// <summary>
    /// The whole of DEC-013 read from the hunter's side: nothing subtracts, ever.
    /// </summary>
    /// <remarks>
    /// A backlog is a bounty and never a debuff. The most neglected task on the list is the
    /// richest thing on the board, writing it up costs nothing at all, and collecting on it costs
    /// exactly the one stamina any fight costs and nothing else: no gold, no experience, no hit
    /// points, no items, no stamina beyond the one. A previous design agent rebuilt overdue debuffs
    /// from a paraphrase of this ruling, and this is the assertion that would have caught it.
    /// </remarks>
    [Fact]
    public async Task Nothing_on_the_contract_path_ever_subtracts_from_the_hunter()
    {
        var harness = await ArrangeAsync(AlwaysHits());

        var task = await AddTaskAsync(
            harness, "Left for a year", Difficulty.Epic, daysOverdue: 365, tags: ["work"]);

        var before = await CharacterAsync(harness);
        var itemsBefore = await harness.Db.InventoryItems.CountAsync(i => i.UserId == harness.UserId);

        var offer = Assert.Single(
            (await harness.Hunts.BoardAsync(harness.UserId, default)).Offers,
            o => o.Task.Id == task.Id);

        // The worst kept task on the list draws the biggest purse, and the multiplier is capped
        // rather than open ended, so stalling past a month buys nothing more.
        Assert.Equal(BountyRules.MaxPercent, BountyRules.BountyPercent(offer.DaysOverdue));

        var onTime = HuntRules.StatBlock(
            HuntArchetypeCatalog.Find(offer.ArchetypeKey)!, offer.Level, 0, 0);

        Assert.True(offer.Monster.MaxGold > onTime.MaxGold);

        var contract = await AcceptAsync(harness, task.Id);
        var taken = await CharacterAsync(harness);

        // Writing it up costs nothing at all, which is the half a toll would have broken.
        Assert.Equal(before.Stamina, taken.Stamina);
        Assert.Equal(before.Gold, taken.Gold);
        Assert.Equal(before.TotalXp, taken.TotalXp);
        Assert.Equal(before.CurrentHitPoints, taken.CurrentHitPoints);
        Assert.Equal(
            itemsBefore,
            await harness.Db.InventoryItems.CountAsync(i => i.UserId == harness.UserId));

        await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);
        Assert.NotNull(await harness.Hunts.DischargeAsync(harness.UserId, task.Id, default));

        var earned = await CharacterAsync(harness);

        Assert.True((await harness.Hunts.FightAsync(harness.UserId, contract.Id, default)).Ok);

        var opened = await CharacterAsync(harness);

        // The one stamina, and not one thing more.
        Assert.Equal(earned.Stamina - CombatService.StaminaPerEncounter, opened.Stamina);
        Assert.Equal(earned.Gold, opened.Gold);
        Assert.Equal(earned.TotalXp, opened.TotalXp);

        var live = await harness.Combat.ActiveAsync(harness.UserId, default);

        Assert.NotNull(live);
        await WinTheFightAsync(harness, live.Id);

        var paid = await CharacterAsync(harness);

        Assert.True(paid.Gold > opened.Gold);
        Assert.InRange(
            (await harness.Db.Encounters.AsNoTracking().SingleAsync(e => e.Id == live.Id)).GoldAwarded,
            offer.Monster.MinGold,
            offer.Monster.MaxGold);
        Assert.Equal(opened.TotalXp, paid.TotalXp);
        Assert.True(
            await harness.Db.InventoryItems.CountAsync(i => i.UserId == harness.UserId) > itemsBefore);
    }
}
