using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TodoApp.Models.Dice;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

/// <summary>
/// The contract routes over the real wiring, including the boundary the completion route draws.
/// </summary>
/// <remarks>
/// Three steps, and the routes are shaped as them. <c>POST /api/rpg/hunts</c> takes a contract and
/// charges nothing; finishing the task discharges it; <c>POST /api/rpg/hunts/{id}/fight</c> opens
/// the fight for the one stamina every fight costs. The route that no longer exists is the point:
/// there is no way to reach bounty gold, loot or standing from a task that is not done.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class HuntEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private QuestwardAppFactory _factory = null!;
    private HttpClient _alice = null!;
    private HttpClient _bob = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        _factory = new QuestwardAppFactory(postgres.ConnectionString);
        _alice = _factory.CreateClientAs("auth0|alice");
        _bob = _factory.CreateClientAs("auth0|bob");
    }

    public ValueTask DisposeAsync()
    {
        _alice.Dispose();
        _bob.Dispose();
        _factory.Dispose();

        return ValueTask.CompletedTask;
    }

    private static async Task ChooseClassAsync(HttpClient client)
    {
        var response = await client.PutAsJsonAsync(
            "/api/rpg/class", new { classKey = ClassCatalog.Fighter });

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Buys fights the only way anything can: by finishing real work (DEC-003).
    /// </summary>
    private static async Task EarnStaminaAsync(HttpClient client, int tasks = 3)
    {
        for (var i = 0; i < tasks; i++)
        {
            var id = await CreateTaskAsync(client, new { title = "Real work", difficulty = "epic" });
            var completed = await client.PostAsJsonAsync(
                $"/api/tasks/{id}/complete", new { utcOffsetMinutes = 0 });

            completed.EnsureSuccessStatusCode();
        }
    }

    private static async Task<Guid> CreateTaskAsync(HttpClient client, object body)
    {
        var created = await client.PostAsJsonAsync("/api/tasks", body);

        created.EnsureSuccessStatusCode();

        return (await created.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static Task<Guid> CreateOverdueAsync(
        HttpClient client, string title = "File the tax return", int daysOverdue = 12, string tag = "work") =>
        CreateTaskAsync(client, new
        {
            title,
            difficulty = "hard",
            dueDate = DateTimeOffset.UtcNow.AddDays(-daysOverdue).AddHours(-1),
            tags = new[] { tag }
        });

    /// <summary>Takes the contract, and asserts what taking one is allowed to cost: nothing.</summary>
    private static async Task<HuntContractDto> AcceptHuntAsync(HttpClient client, Guid taskId)
    {
        var accepted = await client.PostAsJsonAsync("/api/rpg/hunts", new { taskId });

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);

        var contract = (await accepted.Content.ReadFromJsonAsync<HuntContractDto>())!;

        // Created at the contract rather than at an encounter, because what this makes is a
        // promise. The fight is a separate call and only opens once the work is done.
        Assert.Equal($"/api/rpg/hunts/{contract.Id}", accepted.Headers.Location?.ToString());
        Assert.Equal("accepted", contract.Status);
        Assert.Equal(1, contract.StaminaCost);

        return contract;
    }

    /// <summary>Opens the fight a discharged contract earned.</summary>
    private static async Task<HuntDto> FightHuntAsync(HttpClient client, Guid contractId)
    {
        var opened = await client.PostAsync($"/api/rpg/hunts/{contractId}/fight", null);

        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);

        var hunt = (await opened.Content.ReadFromJsonAsync<HuntDto>())!;

        Assert.Equal(
            $"/api/rpg/encounters/{hunt.Encounter.Id}", opened.Headers.Location?.ToString());

        Assert.Equal(hunt.Encounter.Id, hunt.EncounterId);
        Assert.Equal(contractId, hunt.ContractId);

        return hunt;
    }

    private static async Task<CompleteResponse> CompleteAsync(HttpClient client, Guid taskId)
    {
        var completed = await client.PostAsJsonAsync(
            $"/api/tasks/{taskId}/complete", new { utcOffsetMinutes = 0 });

        completed.EnsureSuccessStatusCode();

        return (await completed.Content.ReadFromJsonAsync<CompleteResponse>())!;
    }

    /// <summary>Accept, finish the work, take the fight. The whole honest sequence, over the wire.</summary>
    private static async Task<HuntDto> RunTheWholeWayAsync(HttpClient client, Guid taskId)
    {
        var contract = await AcceptHuntAsync(client, taskId);
        var completed = await CompleteAsync(client, taskId);

        Assert.NotNull(completed.Hunt);
        Assert.Equal("discharged", completed.Hunt.Status);

        return await FightHuntAsync(client, contract.Id);
    }

    [Fact]
    public async Task Every_contract_route_requires_authentication()
    {
        using var anonymous = _factory.CreateAnonymousClient();
        var id = Guid.NewGuid();

        Assert.Equal(
            HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/rpg/hunts")).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/rpg/hunts/active")).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/rpg/hunts", new { taskId = id })).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync($"/api/rpg/hunts/{id}/fight", null)).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.DeleteAsync($"/api/rpg/hunts/{id}")).StatusCode);
    }

    /// <summary>The board is a read: it rolls nothing, writes nothing and costs no stamina.</summary>
    [Fact]
    public async Task The_contract_board_prices_every_open_task_and_charges_for_none_of_it()
    {
        await ChooseClassAsync(_alice);
        await EarnStaminaAsync(_alice);

        var taskId = await CreateOverdueAsync(_alice, daysOverdue: 40);

        var board = await _alice.GetFromJsonAsync<HuntBoardDto>("/api/rpg/hunts");

        Assert.NotNull(board);
        Assert.Equal(1, board.StaminaPerHunt);
        Assert.Equal(FactionCatalog.All.Count, board.Factions.Count);
        Assert.Empty(board.Contracts);

        var offer = Assert.Single(board.Offers, o => o.TaskId == taskId);

        Assert.Equal(40, offer.DaysOverdue);
        Assert.Equal(200, offer.BountyPercent);
        Assert.Equal(HuntArchetypeCatalog.Dread, offer.ArchetypeKey);
        Assert.Equal(FactionCatalog.TheLedger, offer.FactionKey);
        Assert.Equal("The Ledger", offer.FactionName);
        Assert.Equal("unknown", offer.Standing);
        Assert.True(offer.PaysContractReward);
        Assert.Equal(1, offer.StaminaCost);
        Assert.True(offer.MaxGold >= offer.MinGold);
        Assert.False(string.IsNullOrWhiteSpace(offer.MonsterName));

        // The finished tasks that paid for the stamina are not on offer, and neither is the
        // board a second copy of the task list.
        Assert.Single(board.Offers);

        // Read twice, quoted the same, and nothing moved on the character sheet.
        var sheet = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");
        var again = await _alice.GetFromJsonAsync<HuntBoardDto>("/api/rpg/hunts");

        Assert.Equal(offer.MaxGold, again!.Offers[0].MaxGold);
        Assert.Equal(sheet!.Stamina, (await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet"))!.Stamina);
        Assert.Equal(HttpStatusCode.NoContent, (await _alice.GetAsync("/api/rpg/hunts/active")).StatusCode);
    }

    /// <summary>
    /// Taking a contract is free and opens no fight, and the fight waits for the work.
    /// </summary>
    /// <remarks>
    /// The headline rule of the phase, asserted end to end. A contract used to be an encounter
    /// opened on the spot, so taking one cost a stamina and the creature could be killed while the
    /// chore it stood for went on being ignored. Charging to accept is a toll for having a backlog
    /// and paying out for the kill is a reward for keeping one, and DEC-013 has room for neither.
    /// </remarks>
    [Fact]
    public async Task Taking_a_contract_is_free_and_the_fight_waits_for_the_work()
    {
        await ChooseClassAsync(_alice);
        await EarnStaminaAsync(_alice);

        var taskId = await CreateOverdueAsync(_alice, "Clear the gutters", 3);

        var before = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");
        var contract = await AcceptHuntAsync(_alice, taskId);

        Assert.Equal(taskId, contract.TaskId);
        Assert.Equal("Clear the gutters", contract.TaskTitle);
        Assert.Equal(3, contract.DaysOverdue);
        Assert.Null(contract.DischargedAt);

        var after = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");

        // Not a stamina, not a coin, and no fight anywhere.
        Assert.Equal(before!.Stamina, after!.Stamina);
        Assert.Equal(before.Gold, after.Gold);
        Assert.Equal(
            HttpStatusCode.NoContent, (await _alice.GetAsync("/api/rpg/hunts/active")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent, (await _alice.GetAsync("/api/rpg/encounters/active")).StatusCode);

        // The fight is refused while the task is outstanding, and the refusal costs nothing.
        var early = await _alice.PostAsync($"/api/rpg/hunts/{contract.Id}/fight", null);

        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal(
            before.Stamina, (await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet"))!.Stamina);

        // The board shows it as taken rather than as on offer.
        var board = await _alice.GetFromJsonAsync<HuntBoardDto>("/api/rpg/hunts");

        Assert.DoesNotContain(board!.Offers, offer => offer.TaskId == taskId);
        Assert.Equal("accepted", Assert.Single(board.Contracts).Status);

        // Doing the work is the only thing that opens it.
        var completed = await CompleteAsync(_alice, taskId);

        Assert.NotNull(completed.Hunt);
        Assert.Equal("discharged", completed.Hunt.Status);
        Assert.Equal(contract.Id, completed.Hunt.Id);
        Assert.NotNull(completed.Hunt.DischargedAt);
        Assert.True(completed.XpGained > 0);

        // Read after the work, because the work is what pays for the fight (DEC-003).
        var earned = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");

        var hunt = await FightHuntAsync(_alice, contract.Id);

        Assert.Equal(
            earned!.Stamina - 1, (await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet"))!.Stamina);

        Assert.Equal("active", hunt.Encounter.Status);
        Assert.Equal(hunt.Encounter.MonsterMaxHitPoints, hunt.Encounter.MonsterHitPoints);

        // The task's own words appear once, in the opening line, and nowhere else.
        Assert.Contains(hunt.Encounter.Log, line => line.Text.Contains("Clear the gutters"));
        Assert.DoesNotContain("Clear the gutters", hunt.Encounter.MonsterName);

        var resumed = await _alice.GetFromJsonAsync<HuntDto>("/api/rpg/hunts/active");

        Assert.Equal(hunt.Encounter.Id, resumed!.Encounter.Id);

        // Driven by the ordinary routes, which is why there is no attack route of its own.
        var attacked = await _alice.PostAsync(
            $"/api/rpg/encounters/{hunt.Encounter.Id}/attack", null);

        attacked.EnsureSuccessStatusCode();

        Assert.True((await attacked.Content.ReadFromJsonAsync<AttackDto>())!.Encounter.Round >= 1);

        // Ended by the ordinary route too, and it lands on the ordinary chronicle: nothing about
        // the history screen had to learn what a contract is.
        await _alice.PostAsync($"/api/rpg/encounters/{hunt.Encounter.Id}/flee", null);

        var chronicle = await _alice.GetFromJsonAsync<ChronicleDto>("/api/rpg/encounters");
        var recorded = Assert.Single(chronicle!.Encounters, e => e.Id == hunt.Encounter.Id);

        Assert.Equal("fled", recorded.Status);
        Assert.Equal(hunt.Encounter.MonsterName, recorded.MonsterName);
    }

    /// <summary>
    /// Both completion routes discharge the contract they finished, and neither pays for it.
    /// </summary>
    /// <remarks>
    /// The drag to Done and the Done button are two doors into the same thing and only one of
    /// them was widened by hand. Discharging moves no gold, which is what keeps it safe to run
    /// outside the completion's transaction: the purse stays inside the creature until somebody
    /// spends a stamina on it.
    /// </remarks>
    [Fact]
    public async Task Both_completion_routes_discharge_the_contract_and_pay_nothing_for_it()
    {
        await ChooseClassAsync(_alice);
        await EarnStaminaAsync(_alice);

        var pressed = await CreateOverdueAsync(_alice, "Pressed Done", 9);

        await AcceptHuntAsync(_alice, pressed);

        var before = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");
        var completed = await CompleteAsync(_alice, pressed);

        Assert.NotNull(completed.Hunt);
        Assert.Equal("discharged", completed.Hunt.Status);
        Assert.True(completed.XpGained > 0);

        var afterPressing = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");

        // The work paid its own stamina. The contract paid nothing at all.
        Assert.True(afterPressing!.Stamina > before!.Stamina);
        Assert.Equal(before.Gold, afterPressing.Gold);
        Assert.Equal(
            HttpStatusCode.NoContent, (await _alice.GetAsync("/api/rpg/hunts/active")).StatusCode);

        var dragged = await CreateOverdueAsync(_alice, "Dragged to Done", 9);

        await AcceptHuntAsync(_alice, dragged);

        var response = await _alice.PutAsJsonAsync(
            $"/api/tasks/{dragged}/status", new { status = "completed", utcOffsetMinutes = 0 });

        response.EnsureSuccessStatusCode();

        var status = (await response.Content.ReadFromJsonAsync<StatusResponse>())!;

        Assert.NotNull(status.Hunt);
        Assert.Equal("discharged", status.Hunt.Status);
        Assert.Equal(
            before.Gold, (await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet"))!.Gold);

        // A completion with no contract on it reports null rather than an empty object, so the
        // widened field is invisible to every client that has not learned about contracts.
        var plain = await CreateTaskAsync(_alice, new { title = "Nothing riding on it" });

        Assert.Null((await CompleteAsync(_alice, plain)).Hunt);
    }

    /// <summary>
    /// The completion path draws no die at all, which is what makes its boundary uneventful.
    /// </summary>
    /// <remarks>
    /// The architectural test, and the phase changed what it has to prove. Settling a contract
    /// used to pay gold, roll a drop, roll a banner reward and advance quests, all after the
    /// completion had committed and outside its transaction, so a thrown die there had to be
    /// proved harmless to the work. A completion now only discharges, which writes a status and a
    /// timestamp and rolls nothing, so that whole class of failure is gone rather than caught.
    /// <para>
    /// Asserted with a roller armed to throw on the very next die, so any rolling work that crept
    /// back into the completion path would take this test down rather than hide in it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_completion_and_the_discharge_riding_it_draw_no_dice()
    {
        var dice = new FailingDiceRoller(new FixedDiceRoller(1));

        using var poisoned = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IDiceRoller>(dice)));

        using var client = AuthenticatedClient(poisoned, "auth0|alice");

        await ChooseClassAsync(client);
        await EarnStaminaAsync(client);

        var taskId = await CreateOverdueAsync(client, "The one that matters", 11);
        var contract = await AcceptHuntAsync(client, taskId);

        var before = await client.GetFromJsonAsync<CharacterDto>("/api/character");

        dice.Armed = true;

        var completed = await client.PostAsJsonAsync(
            $"/api/tasks/{taskId}/complete", new { utcOffsetMinutes = 0 });

        // Not a 500, and not a refusal: the completion and the discharge riding it both went
        // through with the tray on the floor.
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);

        var body = (await completed.Content.ReadFromJsonAsync<CompleteResponse>())!;

        Assert.NotNull(body.Hunt);
        Assert.Equal("discharged", body.Hunt.Status);
        Assert.Equal(contract.Id, body.Hunt.Id);
        Assert.True(body.XpGained > 0);
        Assert.True(body.Task.IsCompleted);

        var after = await client.GetFromJsonAsync<CharacterDto>("/api/character");

        Assert.Equal(before!.TotalXp + body.XpGained, after!.TotalXp);
        Assert.Equal(before.TasksCompleted + 1, after.TasksCompleted);

        dice.Armed = false;

        // And the contract survived the whole thing, still owed and still collectable.
        var board = await client.GetFromJsonAsync<HuntBoardDto>("/api/rpg/hunts");

        Assert.Equal("discharged", Assert.Single(board!.Contracts).Status);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync(
            $"/api/rpg/hunts/{contract.Id}/fight", null)).StatusCode);
    }

    /// <summary>Every refusal the routes can produce, and the status code it arrives as.</summary>
    [Fact]
    public async Task A_contract_that_cannot_be_taken_or_fought_is_refused_with_the_right_answer()
    {
        await ChooseClassAsync(_alice);
        await EarnStaminaAsync(_alice);

        var parent = await CreateOverdueAsync(_alice, "The whole job", 8);

        var child = await CreateTaskAsync(
            _alice, new { title = "A piece of it", parentId = parent, difficulty = "hard" });

        // A subtask is a bad request: no amount of waiting changes the answer (DEC-014).
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await _alice.PostAsJsonAsync("/api/rpg/hunts", new { taskId = child })).StatusCode);

        // Somebody else's task, and one that never existed, answer identically.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _alice.PostAsJsonAsync("/api/rpg/hunts", new { taskId = Guid.NewGuid() })).StatusCode);

        // A contract id that names nothing is a 404, so ids cannot be probed for existence.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _alice.PostAsync($"/api/rpg/hunts/{Guid.NewGuid()}/fight", null)).StatusCode);

        var contract = await AcceptHuntAsync(_alice, parent);

        // Already taken.
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await _alice.PostAsJsonAsync("/api/rpg/hunts", new { taskId = parent })).StatusCode);

        // The work is not done, so the fight is shut. This is the refusal the phase exists for.
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await _alice.PostAsync($"/api/rpg/hunts/{contract.Id}/fight", null)).StatusCode);

        await CompleteAsync(_alice, parent);

        // Drawn from the tavern's own list rather than named, because the band check runs before
        // the one-fight check and a monster out of band would answer 400 for the wrong reason.
        var tavern = (await _alice.GetFromJsonAsync<List<MonsterDto>>("/api/rpg/monsters"))!;

        Assert.NotEmpty(tavern);

        var hunt = await FightHuntAsync(_alice, contract.Id);

        // A second fight of any kind while one is live.
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await _alice.PostAsJsonAsync(
                "/api/rpg/encounters", new { monsterKey = tavern[0].Key })).StatusCode);

        // A contract buys one fight, and asking twice buys nothing.
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await _alice.PostAsync($"/api/rpg/hunts/{contract.Id}/fight", null)).StatusCode);

        await _alice.PostAsync($"/api/rpg/encounters/{hunt.Encounter.Id}/flee", null);

        // A contract walked away from is worth no standing, because standing counts wins.
        var board = await _alice.GetFromJsonAsync<HuntBoardDto>("/api/rpg/hunts");

        Assert.Equal(
            0, Assert.Single(board!.Factions, f => f.Key == FactionCatalog.TheLedger).WonHunts);

        // A task already finished has nothing left to promise.
        var done = await CreateOverdueAsync(_alice, "Already done", 30);

        await CompleteAsync(_alice, done);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await _alice.PostAsJsonAsync("/api/rpg/hunts", new { taskId = done })).StatusCode);

        // A closed contract cannot be torn up either, because there is nothing left to tear.
        Assert.Equal(
            HttpStatusCode.Conflict, (await _alice.DeleteAsync($"/api/rpg/hunts/{contract.Id}")).StatusCode);
    }

    /// <summary>
    /// A contract can be torn up for nothing, and taken again from scratch.
    /// </summary>
    /// <remarks>
    /// The way out, and the only way to have a contract re-priced after the task under it has
    /// genuinely changed shape: everything was frozen at acceptance. It cannot be farmed, because
    /// a fresh contract can only be discharged by a completion that postdates it.
    /// </remarks>
    [Fact]
    public async Task A_contract_can_be_torn_up_and_taken_again()
    {
        await ChooseClassAsync(_alice);
        await EarnStaminaAsync(_alice);

        var taskId = await CreateOverdueAsync(_alice, "Changed its shape", 6);
        var first = await AcceptHuntAsync(_alice, taskId);

        var before = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");

        var torn = await _alice.DeleteAsync($"/api/rpg/hunts/{first.Id}");

        Assert.Equal(HttpStatusCode.OK, torn.StatusCode);
        Assert.Equal("abandoned", (await torn.Content.ReadFromJsonAsync<HuntContractDto>())!.Status);

        var after = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");

        Assert.Equal(before!.Stamina, after!.Stamina);
        Assert.Equal(before.Gold, after.Gold);

        var board = await _alice.GetFromJsonAsync<HuntBoardDto>("/api/rpg/hunts");

        Assert.Empty(board!.Contracts);
        Assert.Contains(board.Offers, offer => offer.TaskId == taskId);

        var second = await AcceptHuntAsync(_alice, taskId);

        Assert.NotEqual(first.Id, second.Id);

        // The torn up one buys nothing afterwards, so tearing one up is not a way to hold two.
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await _alice.PostAsync($"/api/rpg/hunts/{first.Id}/fight", null)).StatusCode);

        Assert.Equal(second.Id, (await CompleteAsync(_alice, taskId)).Hunt!.Id);
    }

    /// <summary>
    /// Reopening a task takes back the discharge, by either door.
    /// </summary>
    /// <remarks>
    /// A discharged contract is the record of work that was done, and reopening says it was not.
    /// Without this, "finish it, undo it, collect the bounty" would pay gold, loot and standing on
    /// a task that ends the sequence unfinished, which is the shape DEC-013 exists to refuse.
    /// Nothing is confiscated: finishing it again earns it back.
    /// </remarks>
    [Fact]
    public async Task Reopening_a_task_takes_back_the_discharge_on_its_contract()
    {
        await ChooseClassAsync(_alice);
        await EarnStaminaAsync(_alice);

        foreach (var byDragging in (bool[])[false, true])
        {
            var taskId = await CreateOverdueAsync(_alice, $"Undone {byDragging}", 14);
            var contract = await AcceptHuntAsync(_alice, taskId);

            Assert.Equal("discharged", (await CompleteAsync(_alice, taskId)).Hunt!.Status);

            if (byDragging)
            {
                var dragged = await _alice.PutAsJsonAsync(
                    $"/api/tasks/{taskId}/status", new { status = "todo", utcOffsetMinutes = 0 });

                dragged.EnsureSuccessStatusCode();
            }
            else
            {
                (await _alice.PostAsync($"/api/tasks/{taskId}/reopen", null)).EnsureSuccessStatusCode();
            }

            var board = await _alice.GetFromJsonAsync<HuntBoardDto>("/api/rpg/hunts");

            Assert.Equal(
                "accepted", Assert.Single(board!.Contracts, c => c.Id == contract.Id).Status);

            Assert.Equal(
                HttpStatusCode.Conflict,
                (await _alice.PostAsync($"/api/rpg/hunts/{contract.Id}/fight", null)).StatusCode);

            // Earned back by doing it again, so nothing was taken away.
            Assert.Equal("discharged", (await CompleteAsync(_alice, taskId)).Hunt!.Status);
        }
    }

    /// <summary>
    /// Deleting a task tears up the promise and leaves the earned fight standing.
    /// </summary>
    /// <remarks>
    /// Two live states and two answers. An accepted contract is a promise to finish this task, and
    /// deleting the task deletes the only thing that could discharge it; nothing was spent on it,
    /// so nothing is taken. A discharged one keeps its fight, because the work was done and tidying
    /// the row away afterwards must not take back what doing it earned.
    /// </remarks>
    [Fact]
    public async Task Deleting_a_task_tears_up_a_waiting_contract_and_keeps_an_earned_one()
    {
        await ChooseClassAsync(_alice);
        await EarnStaminaAsync(_alice);

        var waiting = await CreateOverdueAsync(_alice, "Never finished", 45);
        var earned = await CreateOverdueAsync(_alice, "Finished first", 45);

        var promise = await AcceptHuntAsync(_alice, waiting);
        var owed = await AcceptHuntAsync(_alice, earned);

        await CompleteAsync(_alice, earned);

        (await _alice.DeleteAsync($"/api/tasks/{waiting}")).EnsureSuccessStatusCode();
        (await _alice.DeleteAsync($"/api/tasks/{earned}")).EnsureSuccessStatusCode();

        var board = await _alice.GetFromJsonAsync<HuntBoardDto>("/api/rpg/hunts");

        // The promise is gone with the task it was made about.
        Assert.DoesNotContain(board!.Contracts, c => c.Id == promise.Id);

        var survivor = Assert.Single(board.Contracts, c => c.Id == owed.Id);

        Assert.Equal("discharged", survivor.Status);
        Assert.Null(survivor.TaskId);
        Assert.Equal("Finished first", survivor.TaskTitle);
        Assert.Equal(45, survivor.DaysOverdue);

        Assert.Equal(
            HttpStatusCode.Conflict,
            (await _alice.PostAsync($"/api/rpg/hunts/{promise.Id}/fight", null)).StatusCode);

        // Still collectable, and still worth exactly what it was written for.
        var hunt = await FightHuntAsync(_alice, owed.Id);

        Assert.Null(hunt.TaskId);
        Assert.Equal(45, hunt.DaysOverdue);
        Assert.Equal("Finished first", hunt.TaskTitle);
    }

    /// <summary>
    /// Not one contract route moves experience or level (DEC-012).
    /// </summary>
    /// <remarks>
    /// The whole feature turns a backlog into gold, loot and standing. The one number it must
    /// never reach is the one that compounds, because a backlog that paid experience would make
    /// the todo list optional, which is what the design is arranged to prevent.
    /// </remarks>
    [Fact]
    public async Task No_contract_route_moves_experience_or_level()
    {
        await ChooseClassAsync(_alice);
        await EarnStaminaAsync(_alice, tasks: 4);

        var taskId = await CreateOverdueAsync(_alice, "Fought after finishing", 60);

        Assert.NotNull(await _alice.GetFromJsonAsync<HuntBoardDto>("/api/rpg/hunts"));

        var contract = await AcceptHuntAsync(_alice, taskId);
        var completed = await CompleteAsync(_alice, taskId);

        // Measured after the completion, so the XP the work paid is not confused with anything
        // the contract paid.
        var before = await _alice.GetFromJsonAsync<CharacterDto>("/api/character");

        Assert.True(completed.XpGained > 0);

        var hunt = await FightHuntAsync(_alice, contract.Id);

        Assert.NotNull(await _alice.GetFromJsonAsync<HuntDto>("/api/rpg/hunts/active"));

        // Fought on real dice, so the ending is not scripted. Which ending it is does not matter
        // to what is being asserted: no ending of a contract may move experience, and a fight
        // that goes badly must not either.
        var ending = "active";

        for (var round = 0; round < 60 && ending == "active"; round++)
        {
            var attacked = await _alice.PostAsync(
                $"/api/rpg/encounters/{hunt.Encounter.Id}/attack", null);

            attacked.EnsureSuccessStatusCode();

            ending = (await attacked.Content.ReadFromJsonAsync<AttackDto>())!.Encounter.Status;
        }

        if (ending == "active")
        {
            await _alice.PostAsync($"/api/rpg/encounters/{hunt.Encounter.Id}/flee", null);
        }

        var after = await _alice.GetFromJsonAsync<CharacterDto>("/api/character");

        Assert.Equal(before!.TotalXp, after!.TotalXp);
        Assert.Equal(before.Level, after.Level);
        Assert.Equal(before.TasksCompleted, after.TasksCompleted);
    }

    /// <summary>Nothing on the contract routes crosses from one adventurer to another.</summary>
    [Fact]
    public async Task One_adventurer_can_neither_see_nor_take_another_s_contracts()
    {
        await ChooseClassAsync(_alice);
        await ChooseClassAsync(_bob);
        await EarnStaminaAsync(_alice);
        await EarnStaminaAsync(_bob);

        var hers = await CreateOverdueAsync(_alice, "Alice's backlog", 30);
        var his = await CreateOverdueAsync(_bob, "Bob's backlog", 1, tag: "home");

        var herContract = await AcceptHuntAsync(_alice, hers);

        // Bob's board is his own list, priced under his own banners.
        var board = await _bob.GetFromJsonAsync<HuntBoardDto>("/api/rpg/hunts");

        Assert.DoesNotContain(board!.Offers, offer => offer.TaskId == hers);
        Assert.Contains(board.Offers, offer => offer.TaskId == his);
        Assert.Empty(board.Contracts);

        var staminaBefore = (await _bob.GetFromJsonAsync<SheetDto>("/api/rpg/sheet"))!.Stamina;

        // Her task and her contract answer to him exactly as ones that never existed.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _bob.PostAsJsonAsync("/api/rpg/hunts", new { taskId = hers })).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _bob.PostAsync($"/api/rpg/hunts/{herContract.Id}/fight", null)).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _bob.DeleteAsync($"/api/rpg/hunts/{herContract.Id}")).StatusCode);

        Assert.Equal(
            staminaBefore, (await _bob.GetFromJsonAsync<SheetDto>("/api/rpg/sheet"))!.Stamina);

        // He can still run his own the whole way, which is what proves the refusals above were
        // about ownership rather than about the rules.
        await RunTheWholeWayAsync(_bob, his);
        await CompleteAsync(_alice, hers);
        await FightHuntAsync(_alice, herContract.Id);

        // Her live fight is not his live fight.
        var hisFight = await _bob.GetFromJsonAsync<HuntDto>("/api/rpg/hunts/active");
        var herFight = await _alice.GetFromJsonAsync<HuntDto>("/api/rpg/hunts/active");

        Assert.NotEqual(hisFight!.EncounterId, herFight!.EncounterId);
        Assert.Equal("Bob's backlog", hisFight.TaskTitle);
        Assert.Equal("Alice's backlog", herFight.TaskTitle);
    }

    /// <summary>
    /// One banner however the tag was typed, beside a tag filter that is deliberately not.
    /// </summary>
    /// <remarks>
    /// The contrast is the point. <c>GET /api/tasks?tag=work</c> is byte-exact Postgres array
    /// containment and finds one of these three; reusing that shape for factions would split one
    /// banner into three and nobody would ever be Trusted anywhere.
    /// </remarks>
    [Fact]
    public async Task Three_spellings_of_one_tag_reach_one_banner_though_the_tag_filter_is_exact()
    {
        await ChooseClassAsync(_alice);
        await EarnStaminaAsync(_alice);

        foreach (var typed in (string[])["Work", "work", "WORK"])
        {
            await CreateTaskAsync(_alice, new
            {
                title = $"Tagged {typed}",
                difficulty = "medium",
                dueDate = DateTimeOffset.UtcNow.AddDays(-5),
                tags = new[] { typed }
            });
        }

        var exact = await _alice.GetFromJsonAsync<List<TaskDto>>("/api/tasks?tag=work");

        Assert.Single(exact!);

        var board = await _alice.GetFromJsonAsync<HuntBoardDto>("/api/rpg/hunts");
        var tagged = board!.Offers.Where(o => o.Title.StartsWith("Tagged", StringComparison.Ordinal)).ToList();

        Assert.Equal(3, tagged.Count);
        Assert.All(tagged, offer => Assert.Equal(FactionCatalog.TheLedger, offer.FactionKey));
        Assert.All(tagged, offer => Assert.Equal("The Ledger", offer.FactionName));
    }

    /// <summary>
    /// A credentialled client for a factory derived by <c>WithWebHostBuilder</c>.
    /// </summary>
    /// <remarks>
    /// The derived factory is a plain WebApplicationFactory rather than the sealed test one, so
    /// it has no CreateClientAs of its own and the headers are added here instead. The derived
    /// builder runs after the base ConfigureWebHost, which is what lets a roller registered in it
    /// win over the production SecureDiceRoller.
    /// </remarks>
    private static HttpClient AuthenticatedClient(
        WebApplicationFactory<Program> factory, string subject)
    {
        var client = factory.CreateClient();

        client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, subject);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return client;
    }

    // ---------------------------------------------------------------- mirrors
    //
    // Hand-written so a DTO that changes shape fails to deserialise here rather than silently
    // dropping a field. Subsets on purpose: only what these tests read.

    private sealed record IdDto(Guid Id);

    private sealed record TaskDto(Guid Id, string Title, bool IsCompleted, int XpAwarded, int DaysOverdue);

    private sealed record CharacterDto(int Level, int TotalXp, int TasksCompleted);

    private sealed record SheetDto(int Stamina, int Gold);

    private sealed record LogDto(string Actor, string Kind, string Text);

    private sealed record StatusEffectDto(string Kind, string Target, int Rounds, int Magnitude, string Source);

    private sealed record EncounterDto(
        Guid Id,
        string MonsterKey,
        string MonsterName,
        int MonsterHitPoints,
        int MonsterMaxHitPoints,
        string Status,
        int Round,
        List<StatusEffectDto> Effects,
        int GoldAwarded,
        List<LogDto> Log);

    private sealed record AttackDto(EncounterDto Encounter);

    private sealed record SummaryDto(int Fought, int Won);

    private sealed record ChronicleDto(SummaryDto Summary, List<EncounterDto> Encounters);

    private sealed record MonsterDto(string Key, string Name, int Level, int StaminaCost);

    private sealed record HuntOfferDto(
        Guid TaskId,
        string Title,
        string Difficulty,
        DateTimeOffset? DueDate,
        int DaysOverdue,
        int Subtasks,
        string ArchetypeKey,
        string MonsterName,
        int Level,
        int MaxHitPoints,
        int MinGold,
        int MaxGold,
        int DropChance,
        int BountyPercent,
        string? FactionKey,
        string? FactionName,
        string? FactionTitle,
        string Standing,
        string RewardFloor,
        bool PaysContractReward,
        int StaminaCost);

    private sealed record FactionStandingDto(
        string Key, string Name, string Standing, string Title, int WonHunts, string RewardFloor);

    private sealed record HuntContractDto(
        Guid Id,
        string Status,
        Guid? TaskId,
        string TaskTitle,
        string ArchetypeKey,
        string MonsterName,
        int Level,
        int MaxHitPoints,
        int MinGold,
        int MaxGold,
        int DaysOverdue,
        int Subtasks,
        int BountyPercent,
        string? FactionKey,
        string? FactionName,
        string Standing,
        string RewardFloor,
        bool PaysContractReward,
        int StaminaCost,
        DateTimeOffset AcceptedAt,
        DateTimeOffset? DischargedAt);

    private sealed record HuntBoardDto(
        List<HuntOfferDto> Offers,
        List<HuntContractDto> Contracts,
        List<FactionStandingDto> Factions,
        int Stamina,
        int StaminaPerHunt);

    private sealed record HuntDto(
        Guid EncounterId,
        Guid? ContractId,
        Guid? TaskId,
        string? TaskTitle,
        string ArchetypeKey,
        string MonsterName,
        int Level,
        int DaysOverdue,
        int Subtasks,
        int BountyPercent,
        string? FactionKey,
        string? FactionName,
        string? FactionTitle,
        string Standing,
        EncounterDto Encounter);

    /// <summary>
    /// The completion response, widened by this phase with a trailing nullable contract.
    /// </summary>
    /// <remarks>
    /// Mirrored whole rather than as the usual subset, because the added field is the wire change
    /// the phase makes and a mirror that stopped short of it would not see it move.
    /// </remarks>
    private sealed record CompleteResponse(
        TaskDto Task,
        int XpGained,
        CharacterDto Character,
        bool LeveledUp,
        int PreviousLevel,
        List<AchievementDto> UnlockedAchievements,
        HuntContractDto? Hunt);

    private sealed record StatusResponse(
        TaskDto Task,
        int XpDelta,
        CharacterDto Character,
        bool LeveledUp,
        bool LeveledDown,
        int PreviousLevel,
        List<AchievementDto> UnlockedAchievements,
        HuntContractDto? Hunt);

    private sealed record AchievementDto(string Key, string Name);
}
