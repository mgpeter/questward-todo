using System.Net;
using System.Net.Http.Json;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Isolation;

/// <summary>
/// The point of the whole spec: two accounts on one instance must not be able to see or
/// touch each other's data through any path.
/// </summary>
/// <remarks>
/// A missed <c>UserId</c> filter does not throw or fail loudly. It leaks data, or lets one
/// user's activity unlock another's badges. These tests are the only thing standing
/// between a one-line omission and that outcome.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class UserIsolationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private QuestwardAppFactory _factory = null!;
    private HttpClient _alice = null!;
    private HttpClient _bob = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        _factory = new QuestwardAppFactory(postgres.ConnectionString);
        _alice = _factory.CreateClientAs("auth0|alice", "alice@example.com", "Alice");
        _bob = _factory.CreateClientAs("auth0|bob", "bob@example.com", "Bob");
    }

    public ValueTask DisposeAsync()
    {
        _alice.Dispose();
        _bob.Dispose();
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    private static async Task<TaskDto> CreateTaskAsync(HttpClient client, string title, string difficulty)
    {
        var response = await client.PostAsJsonAsync("/api/tasks", new { title, difficulty });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TaskDto>())!;
    }

    [Fact]
    public async Task One_users_tasks_never_appear_in_anothers_list()
    {
        await CreateTaskAsync(_alice, "Alice's secret plan", "medium");
        await CreateTaskAsync(_alice, "Alice's other plan", "easy");

        var bobsTasks = await _bob.GetFromJsonAsync<List<TaskDto>>("/api/tasks");

        Assert.Empty(bobsTasks!);

        var alicesTasks = await _alice.GetFromJsonAsync<List<TaskDto>>("/api/tasks");
        Assert.Equal(2, alicesTasks!.Count);
    }

    [Fact]
    public async Task Filters_and_search_do_not_leak_across_users()
    {
        await CreateTaskAsync(_alice, "Alice's findable thing", "epic");

        Assert.Empty((await _bob.GetFromJsonAsync<List<TaskDto>>("/api/tasks?status=open"))!);
        Assert.Empty((await _bob.GetFromJsonAsync<List<TaskDto>>("/api/tasks?status=all"))!);
        Assert.Empty((await _bob.GetFromJsonAsync<List<TaskDto>>("/api/tasks?difficulty=epic"))!);
        Assert.Empty((await _bob.GetFromJsonAsync<List<TaskDto>>("/api/tasks?search=findable"))!);
    }

    [Fact]
    public async Task Another_users_task_is_indistinguishable_from_one_that_does_not_exist()
    {
        var task = await CreateTaskAsync(_alice, "Alice's task", "medium");

        // 404 rather than 403, so ids cannot be probed for existence.
        Assert.Equal(HttpStatusCode.NotFound, (await _bob.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _bob.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _bob.PostAsync($"/api/tasks/{task.Id}/complete", null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _bob.PostAsync($"/api/tasks/{task.Id}/reopen", null)).StatusCode);

        var update = await _bob.PutAsJsonAsync(
            $"/api/tasks/{task.Id}",
            new { title = "Hijacked", difficulty = "easy", priority = "normal" });
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        // And the task is untouched.
        var stillThere = await _alice.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}");
        Assert.Equal("Alice's task", stillThere!.Title);
    }

    [Fact]
    public async Task Reorder_cannot_touch_another_users_tasks()
    {
        var aliceTask = await CreateTaskAsync(_alice, "Alice's task", "medium");
        var bobTask = await CreateTaskAsync(_bob, "Bob's task", "medium");

        // Bob submits Alice's id alongside his own.
        var response = await _bob.PostAsJsonAsync(
            "/api/tasks/reorder",
            new { orderedIds = new[] { aliceTask.Id, bobTask.Id } });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var aliceAfter = await _alice.GetFromJsonAsync<TaskDto>($"/api/tasks/{aliceTask.Id}");
        Assert.Equal(aliceTask.SortOrder, aliceAfter!.SortOrder);
    }

    [Fact]
    public async Task XP_is_awarded_only_to_the_user_who_did_the_work()
    {
        var task = await CreateTaskAsync(_alice, "Alice's epic", "epic");

        var completion = await _alice.PostAsJsonAsync(
            $"/api/tasks/{task.Id}/complete",
            new { utcOffsetMinutes = 0 });
        completion.EnsureSuccessStatusCode();

        var alice = await _alice.GetFromJsonAsync<CharacterDto>("/api/character");
        var bob = await _bob.GetFromJsonAsync<CharacterDto>("/api/character");

        Assert.Equal(100, alice!.TotalXp);
        Assert.Equal(2, alice.Level);
        Assert.Equal(1, alice.TasksCompleted);

        Assert.Equal(0, bob!.TotalXp);
        Assert.Equal(1, bob.Level);
        Assert.Equal(0, bob.TasksCompleted);
    }

    [Fact]
    public async Task Badges_unlock_per_user_not_per_instance()
    {
        // First Blood is the clearest case: with a unique index on AchievementKey alone,
        // Alice earning it would permanently prevent Bob from ever earning it.
        var aliceTask = await CreateTaskAsync(_alice, "Alice's first", "medium");
        await _alice.PostAsJsonAsync($"/api/tasks/{aliceTask.Id}/complete", new { utcOffsetMinutes = 0 });

        var bobBefore = await _bob.GetFromJsonAsync<List<AchievementDto>>("/api/achievements");
        Assert.DoesNotContain(bobBefore!, a => a.Unlocked);

        var bobTask = await CreateTaskAsync(_bob, "Bob's first", "medium");
        var bobCompletion = await _bob.PostAsJsonAsync(
            $"/api/tasks/{bobTask.Id}/complete",
            new { utcOffsetMinutes = 0 });
        bobCompletion.EnsureSuccessStatusCode();

        var result = await bobCompletion.Content.ReadFromJsonAsync<CompleteResponse>();
        Assert.Contains(result!.UnlockedAchievements, a => a.Key == "first-blood");

        var bobAfter = await _bob.GetFromJsonAsync<List<AchievementDto>>("/api/achievements");
        Assert.Contains(bobAfter!, a => a is { Key: "first-blood", Unlocked: true });
    }

    [Fact]
    public async Task Clean_Slate_looks_at_the_callers_board_not_the_instance()
    {
        // Bob keeps tasks open throughout. If the open-task counts were unscoped, Alice
        // clearing her own board would never register as clean.
        await CreateTaskAsync(_bob, "Bob leaves this open", "easy");
        await CreateTaskAsync(_bob, "And this one", "easy");

        var alicesTasks = new List<TaskDto>
        {
            await CreateTaskAsync(_alice, "Alice one", "easy"),
            await CreateTaskAsync(_alice, "Alice two", "easy"),
            await CreateTaskAsync(_alice, "Alice three", "easy")
        };

        HttpResponseMessage? last = null;

        foreach (var task in alicesTasks)
        {
            last = await _alice.PostAsJsonAsync(
                $"/api/tasks/{task.Id}/complete",
                new { utcOffsetMinutes = 0 });
        }

        var result = await last!.Content.ReadFromJsonAsync<CompleteResponse>();

        Assert.Contains(result!.UnlockedAchievements, a => a.Key == "clean-slate");
    }

    [Fact]
    public async Task Giant_Killer_counts_only_the_callers_hard_tasks()
    {
        // Bob grinds out nine Hard tasks. Alice's first Hard task must not be treated as
        // her tenth just because the instance has ten in total.
        for (var i = 0; i < 9; i++)
        {
            var bobTask = await CreateTaskAsync(_bob, $"Bob hard {i}", "hard");
            await _bob.PostAsJsonAsync($"/api/tasks/{bobTask.Id}/complete", new { utcOffsetMinutes = 0 });
        }

        var aliceTask = await CreateTaskAsync(_alice, "Alice hard", "hard");
        var completion = await _alice.PostAsJsonAsync(
            $"/api/tasks/{aliceTask.Id}/complete",
            new { utcOffsetMinutes = 0 });

        var result = await completion.Content.ReadFromJsonAsync<CompleteResponse>();

        Assert.DoesNotContain(result!.UnlockedAchievements, a => a.Key == "giant-killer");
    }

    [Fact]
    public async Task Stats_report_only_the_callers_work()
    {
        await CreateTaskAsync(_bob, "Bob's task", "epic");
        var aliceTask = await CreateTaskAsync(_alice, "Alice's task", "medium");
        await _alice.PostAsJsonAsync($"/api/tasks/{aliceTask.Id}/complete", new { utcOffsetMinutes = 0 });

        var stats = await _alice.GetFromJsonAsync<StatsDto>("/api/stats?utcOffsetMinutes=0");

        Assert.Equal(1, stats!.TotalTasks);
        Assert.Equal(1, stats.CompletedTasks);
        Assert.Equal(0, stats.OpenTasks);
        Assert.Equal(25, stats.TotalXp);

        // Bob's Epic must not show up in Alice's breakdown.
        Assert.Equal(0, stats.ByDifficulty.Single(b => b.Difficulty == "epic").Completed);
        Assert.Equal(1, stats.ByDifficulty.Single(b => b.Difficulty == "medium").Completed);
    }

    [Fact]
    public async Task Renaming_a_character_does_not_rename_anyone_elses()
    {
        var updated = await _alice.PutAsJsonAsync(
            "/api/character",
            new { name = "Alicia", avatarKey = "owl" });
        updated.EnsureSuccessStatusCode();

        var bob = await _bob.GetFromJsonAsync<CharacterDto>("/api/character");

        Assert.Equal("Bob", bob!.Name);
        Assert.Equal("fox", bob.AvatarKey);
    }

    [Fact]
    public async Task The_full_XP_flow_still_behaves_per_user()
    {
        // Phase 0 regression: idempotent completion, exact refunds, badges never revoked,
        // now asserted while a second user is active on the same instance.
        await CreateTaskAsync(_bob, "Bob noise", "epic");

        var task = await CreateTaskAsync(_alice, "Alice's hard task", "hard");

        var first = await (await _alice.PostAsJsonAsync(
            $"/api/tasks/{task.Id}/complete", new { utcOffsetMinutes = 0 }))
            .Content.ReadFromJsonAsync<CompleteResponse>();
        Assert.Equal(50, first!.XpGained);

        var again = await (await _alice.PostAsJsonAsync(
            $"/api/tasks/{task.Id}/complete", new { utcOffsetMinutes = 0 }))
            .Content.ReadFromJsonAsync<CompleteResponse>();
        Assert.Equal(0, again!.XpGained);
        Assert.Equal(50, again.Character.TotalXp);

        var reopened = await (await _alice.PostAsync($"/api/tasks/{task.Id}/reopen", null))
            .Content.ReadFromJsonAsync<ReopenResponse>();
        Assert.Equal(50, reopened!.XpLost);
        Assert.Equal(0, reopened.Character.TotalXp);

        // Badges survive the reopen.
        var achievements = await _alice.GetFromJsonAsync<List<AchievementDto>>("/api/achievements");
        Assert.Contains(achievements!, a => a is { Key: "deep-work", Unlocked: true });

        // Bob was never touched.
        var bob = await _bob.GetFromJsonAsync<CharacterDto>("/api/character");
        Assert.Equal(0, bob!.TotalXp);
    }

    private sealed record TaskDto(Guid Id, string Title, int SortOrder, bool IsCompleted);

    private sealed record CharacterDto(
        string Name,
        string AvatarKey,
        int Level,
        int TotalXp,
        int TasksCompleted);

    private sealed record AchievementDto(string Key, bool Unlocked);

    private sealed record CompleteResponse(
        int XpGained,
        CharacterDto Character,
        bool LeveledUp,
        List<AchievementDto> UnlockedAchievements);

    private sealed record ReopenResponse(int XpLost, CharacterDto Character);

    private sealed record DifficultyBreakdownDto(string Difficulty, int Completed, int XpEarned);

    private sealed record StatsDto(
        int TotalTasks,
        int OpenTasks,
        int CompletedTasks,
        int TotalXp,
        List<DifficultyBreakdownDto> ByDifficulty);
}
