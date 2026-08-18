using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TodoApp.Models;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Tasks;

/// <summary>
/// Clearing finished tasks that are older than the record can see.
/// </summary>
/// <remarks>
/// A repeating task spawns a successor on every completion (DEC-015), so the Done column grows
/// forever and something has to be able to trim it. What must not happen is the obvious
/// version: the record panel is computed from these rows, so a blanket "clear everything
/// finished" would blank the fourteen day chart and the difficulty breakdown, which are the two
/// things that make keeping any of it worthwhile.
/// <para>
/// Most of what is asserted here is therefore about what survives rather than what goes.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class ClearCompletedTests(PostgresFixture postgres) : IAsyncLifetime
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

    private sealed record TaskView(Guid Id, string Title, bool IsCompleted);

    private sealed record ClearedView(int Deleted, int OlderThanDays);

    private sealed record BreakdownView(string Difficulty, int Completed, int XpEarned);

    private sealed record DayView(DateOnly Date, int Completed, int XpEarned);

    private sealed record StatsView(
        int TotalTasks,
        int OpenTasks,
        int CompletedTasks,
        int TotalXp,
        BreakdownView[] ByDifficulty,
        DayView[] Last14Days);

    private sealed record CharacterView(int TotalXp, int TasksCompleted);

    /// <summary>
    /// Writes a finished task straight onto the row, dated. Completing through the endpoint
    /// always stamps "now", and the whole question here is what happens to older ones.
    /// </summary>
    private async Task SeedFinishedAsync(string subject, string title, int daysAgo, Difficulty difficulty)
    {
        await using var db = postgres.CreateContext();
        var user = await db.Users.SingleAsync(u => u.Auth0Sub == subject);
        var completedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo);

        db.Tasks.Add(new TodoTask
        {
            UserId = user.Id,
            Title = title,
            Difficulty = difficulty,
            Status = TaskProgress.Completed,
            CompletedAt = completedAt,
            XpAwarded = difficulty.BaseXp(),
            StaminaAwarded = difficulty.Stamina(),
            CreatedAt = completedAt,
            UpdatedAt = completedAt
        });

        await db.SaveChangesAsync();
    }

    private async Task ProvisionAsync(HttpClient client) =>
        (await client.GetAsync("/api/tasks")).EnsureSuccessStatusCode();

    private async Task<ClearedView> ClearAsync(HttpClient client, string query = "")
    {
        var response = await client.DeleteAsync($"/api/tasks/completed{query}");
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ClearedView>())!;
    }

    // ------------------------------------------------------------------- what goes

    [Fact]
    public async Task Only_tasks_older_than_the_record_can_see_are_cleared()
    {
        await ProvisionAsync(_alice);
        await SeedFinishedAsync("auth0|alice", "Ancient", 90, Difficulty.Medium);
        await SeedFinishedAsync("auth0|alice", "Old", 20, Difficulty.Medium);
        await SeedFinishedAsync("auth0|alice", "Just outside", 15, Difficulty.Medium);
        await SeedFinishedAsync("auth0|alice", "Inside the window", 3, Difficulty.Medium);
        await SeedFinishedAsync("auth0|alice", "Today", 0, Difficulty.Medium);

        var cleared = await ClearAsync(_alice);

        Assert.Equal(3, cleared.Deleted);
        Assert.Equal(14, cleared.OlderThanDays);

        var left = await _alice.GetFromJsonAsync<TaskView[]>("/api/tasks");

        Assert.Equal(
            ["Inside the window", "Today"],
            left!.Select(t => t.Title).OrderBy(title => title));
    }

    [Fact]
    public async Task Clearing_leaves_open_work_alone()
    {
        await ProvisionAsync(_alice);
        await SeedFinishedAsync("auth0|alice", "Long done", 60, Difficulty.Medium);

        (await _alice.PostAsJsonAsync("/api/tasks", new { title = "Still to do" }))
            .EnsureSuccessStatusCode();

        var cleared = await ClearAsync(_alice);

        Assert.Equal(1, cleared.Deleted);

        var left = await _alice.GetFromJsonAsync<TaskView[]>("/api/tasks");
        var only = Assert.Single(left!);

        Assert.Equal("Still to do", only.Title);
    }

    [Fact]
    public async Task Clearing_an_already_clear_list_is_not_an_error()
    {
        await ProvisionAsync(_alice);

        var cleared = await ClearAsync(_alice);

        Assert.Equal(0, cleared.Deleted);
    }

    // -------------------------------------------------------------- what survives

    [Fact]
    public async Task Clearing_never_moves_the_activity_chart_or_the_breakdown()
    {
        // The whole reason this route is scoped rather than a plain "clear done". Both panels
        // are computed from finished rows, so a blanket clear would blank them.
        await ProvisionAsync(_alice);
        await SeedFinishedAsync("auth0|alice", "Ancient", 90, Difficulty.Epic);
        await SeedFinishedAsync("auth0|alice", "Old", 30, Difficulty.Hard);
        await SeedFinishedAsync("auth0|alice", "Recent", 2, Difficulty.Medium);
        await SeedFinishedAsync("auth0|alice", "Today", 0, Difficulty.Easy);

        var before = await _alice.GetFromJsonAsync<StatsView>("/api/stats?utcOffsetMinutes=0");

        // The two outside the window go; the two inside it stay.
        Assert.Equal(2, (await ClearAsync(_alice)).Deleted);

        var after = await _alice.GetFromJsonAsync<StatsView>("/api/stats?utcOffsetMinutes=0");

        // Day for day, byte for byte.
        Assert.Equal(
            before!.Last14Days.Select(d => (d.Date, d.Completed, d.XpEarned)),
            after!.Last14Days.Select(d => (d.Date, d.Completed, d.XpEarned)));

        // The breakdown loses exactly the two that were outside the window and nothing else.
        Assert.Equal(1, after.ByDifficulty.Single(b => b.Difficulty == "medium").Completed);
        Assert.Equal(1, after.ByDifficulty.Single(b => b.Difficulty == "easy").Completed);
        Assert.Equal(0, after.ByDifficulty.Single(b => b.Difficulty == "epic").Completed);
    }

    [Fact]
    public async Task A_smaller_window_than_the_chart_uses_is_refused_rather_than_honoured()
    {
        // The one way this could quietly start eating the chart is a caller asking for a
        // shorter floor, so the floor is clamped rather than trusted.
        await ProvisionAsync(_alice);
        await SeedFinishedAsync("auth0|alice", "Three days ago", 3, Difficulty.Medium);
        await SeedFinishedAsync("auth0|alice", "Ancient", 90, Difficulty.Medium);

        var cleared = await ClearAsync(_alice, "?olderThanDays=1");

        Assert.Equal(14, cleared.OlderThanDays);
        Assert.Equal(1, cleared.Deleted);

        var left = await _alice.GetFromJsonAsync<TaskView[]>("/api/tasks");
        Assert.Equal("Three days ago", Assert.Single(left!).Title);
    }

    [Fact]
    public async Task A_larger_window_is_honoured_because_it_only_keeps_more()
    {
        await ProvisionAsync(_alice);
        await SeedFinishedAsync("auth0|alice", "Forty days", 40, Difficulty.Medium);
        await SeedFinishedAsync("auth0|alice", "Ninety days", 90, Difficulty.Medium);

        var cleared = await ClearAsync(_alice, "?olderThanDays=60");

        Assert.Equal(60, cleared.OlderThanDays);
        Assert.Equal(1, cleared.Deleted);
    }

    [Fact]
    public async Task Clearing_never_takes_back_experience_or_the_count_of_work_done()
    {
        // Matching what deleting a single task has always done. Both are a memory of work
        // actually done rather than a balance, in the way a badge is never revoked.
        await ProvisionAsync(_alice);

        var created = await (await _alice.PostAsJsonAsync(
            "/api/tasks", new { title = "Real work", difficulty = "epic" }))
            .Content.ReadFromJsonAsync<TaskView>();

        (await _alice.PostAsJsonAsync($"/api/tasks/{created!.Id}/complete", new { utcOffsetMinutes = 0 }))
            .EnsureSuccessStatusCode();

        // Age it past the window without going near the character.
        await using (var db = postgres.CreateContext())
        {
            var row = await db.Tasks.SingleAsync(t => t.Id == created.Id);
            row.CompletedAt = DateTimeOffset.UtcNow.AddDays(-40);
            await db.SaveChangesAsync();
        }

        var before = await _alice.GetFromJsonAsync<CharacterView>("/api/character");

        Assert.Equal(1, (await ClearAsync(_alice)).Deleted);

        var after = await _alice.GetFromJsonAsync<CharacterView>("/api/character");

        Assert.Equal(before!.TotalXp, after!.TotalXp);
        Assert.Equal(before.TasksCompleted, after.TasksCompleted);
        Assert.Equal(Difficulty.Epic.BaseXp(), after.TotalXp);
    }

    // ---------------------------------------------------------------- the boundaries

    [Fact]
    public async Task One_persons_clearing_leaves_another_persons_history_alone()
    {
        await ProvisionAsync(_alice);
        await ProvisionAsync(_bob);
        await SeedFinishedAsync("auth0|alice", "Alice's ancient chore", 90, Difficulty.Medium);
        await SeedFinishedAsync("auth0|bob", "Bob's ancient chore", 90, Difficulty.Medium);

        Assert.Equal(1, (await ClearAsync(_alice)).Deleted);

        var bobs = await _bob.GetFromJsonAsync<TaskView[]>("/api/tasks");

        Assert.Equal("Bob's ancient chore", Assert.Single(bobs!).Title);
    }

    [Fact]
    public async Task Clearing_requires_authentication()
    {
        using var anonymous = _factory.CreateAnonymousClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.DeleteAsync("/api/tasks/completed")).StatusCode);
    }
}
