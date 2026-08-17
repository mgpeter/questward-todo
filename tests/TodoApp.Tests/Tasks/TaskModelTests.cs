using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TodoApp.Models;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Tasks;

/// <summary>
/// The task model overhaul: subtasks, tags, recurrence and the three-column status.
///
/// Every test here that looks like it is about a feature is really about DEC-012. Each
/// of these additions is a new way to press the "I finished something" button, and the
/// rule is that XP is paid for the work, not for the pressing. Subtasks must not let one
/// task be split into twenty payouts, and a daily task must not pay twenty times today.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class TaskModelTests(PostgresFixture postgres) : IAsyncLifetime
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

    private sealed record TaskView(
        Guid Id,
        Guid? ParentId,
        string Title,
        string Status,
        bool IsCompleted,
        string[] Tags,
        string Recurrence,
        bool AwardsProgression,
        int XpAwarded,
        DateTimeOffset? StartedAt,
        TaskView[] Subtasks);

    private sealed record CharacterView(int Level, int TotalXp, int TasksCompleted);

    private sealed record CompleteView(TaskView Task, int XpGained, CharacterView Character);

    private sealed record StatusView(TaskView Task, int XpDelta, CharacterView Character);

    private async Task<TaskView> CreateAsync(
        HttpClient client,
        string title,
        string difficulty = "medium",
        Guid? parentId = null,
        string[]? tags = null,
        string recurrence = "none")
    {
        var response = await client.PostAsJsonAsync(
            "/api/tasks",
            new { title, difficulty, parentId, tags, recurrence });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TaskView>())!;
    }

    private async Task<CompleteView> CompleteAsync(HttpClient client, Guid id)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/tasks/{id}/complete", new { utcOffsetMinutes = 0 });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CompleteView>())!;
    }

    private async Task<CharacterView> CharacterAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<CharacterView>("/api/character"))!;

    // ------------------------------------------------------------------ subtasks

    [Fact]
    public async Task A_subtask_pays_nothing_when_completed()
    {
        var parent = await CreateAsync(_alice, "Move house", "epic");
        var child = await CreateAsync(_alice, "Pack the kitchen", "epic", parentId: parent.Id);

        Assert.False(child.AwardsProgression);

        var result = await CompleteAsync(_alice, child.Id);

        Assert.Equal(0, result.XpGained);
        Assert.Equal(0, result.Character.TotalXp);
        Assert.Equal(0, result.Character.TasksCompleted);
        Assert.True(result.Task.IsCompleted);
    }

    [Fact]
    public async Task Splitting_a_task_into_subtasks_cannot_multiply_its_reward()
    {
        var parent = await CreateAsync(_alice, "Move house", "epic");

        for (var i = 0; i < 8; i++)
        {
            var child = await CreateAsync(_alice, $"Box {i}", "epic", parentId: parent.Id);
            await CompleteAsync(_alice, child.Id);
        }

        var afterChildren = await CharacterAsync(_alice);
        Assert.Equal(0, afterChildren.TotalXp);

        await CompleteAsync(_alice, parent.Id);

        var afterParent = await CharacterAsync(_alice);

        // One task's worth of XP, no matter how finely it was diced.
        Assert.Equal(Difficulty.Epic.BaseXp(), afterParent.TotalXp);
        Assert.Equal(1, afterParent.TasksCompleted);
    }

    [Fact]
    public async Task Subtasks_are_nested_under_their_parent_and_never_listed_on_their_own()
    {
        var parent = await CreateAsync(_alice, "Move house");
        await CreateAsync(_alice, "Pack", parentId: parent.Id);
        await CreateAsync(_alice, "Hire a van", parentId: parent.Id);

        var list = await _alice.GetFromJsonAsync<TaskView[]>("/api/tasks");

        var top = Assert.Single(list!);
        Assert.Equal(parent.Id, top.Id);
        Assert.Equal(2, top.Subtasks.Length);
        Assert.All(top.Subtasks, subtask => Assert.Equal(parent.Id, subtask.ParentId));
    }

    [Fact]
    public async Task Nesting_stops_at_one_level()
    {
        var parent = await CreateAsync(_alice, "Move house");
        var child = await CreateAsync(_alice, "Pack", parentId: parent.Id);

        var response = await _alice.PostAsJsonAsync(
            "/api/tasks", new { title = "Pack the mugs", parentId = child.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_task_cannot_be_nested_under_another_users_task()
    {
        var bobs = await CreateAsync(_bob, "Bob's project");

        var response = await _alice.PostAsJsonAsync(
            "/api/tasks", new { title = "Sneak in", parentId = bobs.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_parent_takes_its_subtasks_with_it()
    {
        var parent = await CreateAsync(_alice, "Move house");
        await CreateAsync(_alice, "Pack", parentId: parent.Id);

        (await _alice.DeleteAsync($"/api/tasks/{parent.Id}")).EnsureSuccessStatusCode();

        await using var db = postgres.CreateContext();
        Assert.Empty(await db.Tasks.Where(t => t.ParentId == parent.Id).ToListAsync());
    }

    [Fact]
    public async Task Subtasks_do_not_count_toward_achievements()
    {
        // Clean Slate needs three completions in the local day with nothing left open.
        var parent = await CreateAsync(_alice, "Move house");

        for (var i = 0; i < 5; i++)
        {
            var child = await CreateAsync(_alice, $"Box {i}", parentId: parent.Id);
            await CompleteAsync(_alice, child.Id);
        }

        var result = await CompleteAsync(_alice, parent.Id);

        Assert.Equal(1, result.Character.TasksCompleted);
    }

    // ---------------------------------------------------------------- recurrence

    [Fact]
    public async Task Ticking_a_daily_task_twice_in_its_period_pays_once()
    {
        var task = await CreateAsync(_alice, "Water the plants", "medium", recurrence: "daily");

        var first = await CompleteAsync(_alice, task.Id);
        Assert.Equal(Difficulty.Medium.BaseXp(), first.XpGained);

        for (var i = 0; i < 5; i++)
        {
            var again = await CompleteAsync(_alice, task.Id);
            Assert.Equal(0, again.XpGained);
        }

        var character = await CharacterAsync(_alice);
        Assert.Equal(Difficulty.Medium.BaseXp(), character.TotalXp);
    }

    [Fact]
    public async Task A_daily_task_opens_again_and_pays_again_once_its_period_has_passed()
    {
        var task = await CreateAsync(_alice, "Water the plants", "medium", recurrence: "daily");

        await CompleteAsync(_alice, task.Id);

        // Wind the gate back rather than the clock forward: the gate is the whole
        // mechanism, and moving time in a test proves less than moving the thing under test.
        await using (var db = postgres.CreateContext())
        {
            var row = await db.Tasks.SingleAsync(t => t.Id == task.Id);
            row.XpEligibleFrom = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        // No reopen, no nightly job: it simply reads as open again.
        var open = await _alice.GetFromJsonAsync<TaskView[]>("/api/tasks?status=open");
        Assert.Equal(task.Id, Assert.Single(open!).Id);
        Assert.False(Assert.Single(open!).IsCompleted);

        var tomorrow = await CompleteAsync(_alice, task.Id);

        Assert.Equal(Difficulty.Medium.BaseXp(), tomorrow.XpGained);
        Assert.Equal(Difficulty.Medium.BaseXp() * 2, tomorrow.Character.TotalXp);
    }

    [Fact]
    public async Task Reopening_a_daily_task_hands_the_reward_back_and_lets_it_be_earned_again()
    {
        var task = await CreateAsync(_alice, "Water the plants", "medium", recurrence: "daily");

        await CompleteAsync(_alice, task.Id);

        (await _alice.PostAsJsonAsync($"/api/tasks/{task.Id}/reopen", new { }))
            .EnsureSuccessStatusCode();

        var refunded = await CharacterAsync(_alice);
        Assert.Equal(0, refunded.TotalXp);

        // An accidental tick and untick must not destroy the day's XP with no way to
        // earn it back. The refund reopened the gate, so doing the work pays again.
        var redone = await CompleteAsync(_alice, task.Id);

        Assert.Equal(Difficulty.Medium.BaseXp(), redone.XpGained);
        Assert.Equal(Difficulty.Medium.BaseXp(), redone.Character.TotalXp);
    }

    [Fact]
    public async Task No_sequence_of_ticks_edits_and_reopens_can_unbalance_the_ledger()
    {
        // The invariant that actually protects DEC-012, stated once: the character holds
        // exactly the XP that the completed tasks say they were paid. Recurrence, edits
        // and reopens are each a way to move both halves, so the check is that they move
        // together, not that any individual step behaves a particular way.
        var daily = await CreateAsync(_alice, "Water the plants", "medium", recurrence: "daily");
        var once = await CreateAsync(_alice, "File the tax return", "epic");

        async Task AssertBalancedAsync(string step)
        {
            var character = await CharacterAsync(_alice);

            await using var db = postgres.CreateContext();
            var banked = await db.Tasks
                .Where(t => t.Status == TaskProgress.Completed)
                .SumAsync(t => t.XpAwarded);

            Assert.True(
                character.TotalXp == banked,
                $"After {step}: character holds {character.TotalXp} XP, tasks account for {banked}.");
        }

        await CompleteAsync(_alice, daily.Id);
        await AssertBalancedAsync("completing the daily task");

        await CompleteAsync(_alice, daily.Id);
        await AssertBalancedAsync("ticking it a second time");

        (await _alice.PutAsJsonAsync($"/api/tasks/{daily.Id}", new
        {
            title = "Water the plants",
            difficulty = "epic",
            priority = "normal",
            recurrence = "none"
        })).EnsureSuccessStatusCode();
        await AssertBalancedAsync("raising its difficulty and dropping recurrence");

        (await _alice.PostAsJsonAsync($"/api/tasks/{daily.Id}/reopen", new { }))
            .EnsureSuccessStatusCode();
        await AssertBalancedAsync("reopening it");

        await CompleteAsync(_alice, once.Id);
        await AssertBalancedAsync("completing the one-off task");

        (await _alice.PutAsJsonAsync($"/api/tasks/{daily.Id}", new
        {
            title = "Water the plants",
            difficulty = "epic",
            priority = "normal",
            recurrence = "daily"
        })).EnsureSuccessStatusCode();
        await CompleteAsync(_alice, daily.Id);
        await AssertBalancedAsync("restoring recurrence and completing again");
    }

    [Fact]
    public async Task A_subtask_cannot_be_made_to_recur()
    {
        var parent = await CreateAsync(_alice, "Move house");
        var child = await CreateAsync(
            _alice, "Pack", parentId: parent.Id, recurrence: "daily");

        Assert.Equal("none", child.Recurrence, ignoreCase: true);
    }

    // ---------------------------------------------------------------------- tags

    [Fact]
    public async Task Tags_are_trimmed_deduplicated_and_capped()
    {
        var task = await CreateAsync(
            _alice, "Tagged", tags: ["  Work ", "work", "WORK", "home", new string('x', 40), ""]);

        Assert.Equal(["Work", "home"], task.Tags);
    }

    [Fact]
    public async Task Tasks_can_be_filtered_by_tag_and_the_tag_list_is_scoped_per_user()
    {
        await CreateAsync(_alice, "Standup", tags: ["work"]);
        await CreateAsync(_alice, "Laundry", tags: ["home"]);
        await CreateAsync(_bob, "Bob's thing", tags: ["secret"]);

        var filtered = await _alice.GetFromJsonAsync<TaskView[]>("/api/tasks?tag=work");
        Assert.Equal("Standup", Assert.Single(filtered!).Title);

        var tags = await _alice.GetFromJsonAsync<string[]>("/api/tasks/tags");
        Assert.Equal(["home", "work"], tags);

        var bobsTags = await _bob.GetFromJsonAsync<string[]>("/api/tasks/tags");
        Assert.Equal(["secret"], bobsTags);
    }

    // -------------------------------------------------------------------- status

    [Fact]
    public async Task Moving_a_task_to_in_progress_stamps_it_and_moves_no_experience()
    {
        var task = await CreateAsync(_alice, "Write the report");

        var response = await _alice.PutAsJsonAsync(
            $"/api/tasks/{task.Id}/status", new { status = "inProgress" });

        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<StatusView>())!;

        Assert.Equal(0, result.XpDelta);
        Assert.Equal(0, result.Character.TotalXp);
        Assert.NotNull(result.Task.StartedAt);
        Assert.False(result.Task.IsCompleted);
    }

    [Fact]
    public async Task Dragging_a_task_into_the_done_column_awards_exactly_as_completing_it_does()
    {
        var task = await CreateAsync(_alice, "Write the report", "hard");

        var response = await _alice.PutAsJsonAsync(
            $"/api/tasks/{task.Id}/status", new { status = "completed", utcOffsetMinutes = 0 });

        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<StatusView>())!;

        Assert.Equal(Difficulty.Hard.BaseXp(), result.XpDelta);
        Assert.Equal(Difficulty.Hard.BaseXp(), result.Character.TotalXp);
        Assert.True(result.Task.IsCompleted);
    }

    [Fact]
    public async Task Dragging_a_task_back_out_of_done_takes_the_experience_with_it()
    {
        var task = await CreateAsync(_alice, "Write the report", "hard");

        await _alice.PutAsJsonAsync(
            $"/api/tasks/{task.Id}/status", new { status = "completed", utcOffsetMinutes = 0 });

        var response = await _alice.PutAsJsonAsync(
            $"/api/tasks/{task.Id}/status", new { status = "inProgress" });

        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<StatusView>())!;

        Assert.Equal(-Difficulty.Hard.BaseXp(), result.XpDelta);
        Assert.Equal(0, result.Character.TotalXp);

        // The drag said "in progress", not "todo", and the column has to end up where it
        // was dropped even though reopening on its own lands in todo.
        Assert.Equal("inProgress", result.Task.Status, ignoreCase: true);
        Assert.False(result.Task.IsCompleted);
    }

    [Fact]
    public async Task Setting_the_status_a_task_already_has_is_a_no_op()
    {
        var task = await CreateAsync(_alice, "Write the report", "hard");
        await CompleteAsync(_alice, task.Id);

        var response = await _alice.PutAsJsonAsync(
            $"/api/tasks/{task.Id}/status", new { status = "completed", utcOffsetMinutes = 0 });

        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<StatusView>())!;

        Assert.Equal(0, result.XpDelta);
        Assert.Equal(Difficulty.Hard.BaseXp(), result.Character.TotalXp);
    }

    [Fact]
    public async Task Another_users_task_cannot_be_moved()
    {
        var bobs = await CreateAsync(_bob, "Bob's report");

        var response = await _alice.PutAsJsonAsync(
            $"/api/tasks/{bobs.Id}/status", new { status = "completed" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
