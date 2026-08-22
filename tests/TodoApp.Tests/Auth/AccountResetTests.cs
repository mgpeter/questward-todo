using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Auth;

/// <summary>
/// The danger zone, driven through the real endpoint.
/// </summary>
/// <remarks>
/// Asserted at HTTP altitude rather than against the service, because the two things most likely
/// to go wrong live above it: the confirmation filter, and the scoping that keeps one account's
/// reset off another account's rows.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class AccountResetTests(PostgresFixture postgres) : IAsyncLifetime
{
    private QuestwardAppFactory _factory = null!;
    private HttpClient _owner = null!;
    private HttpClient _stranger = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        _factory = new QuestwardAppFactory(postgres.ConnectionString);
        _owner = _factory.CreateClientAs("auth0|owner", "owner@example.com", "Owner");
        _stranger = _factory.CreateClientAs("auth0|stranger", "stranger@example.com", "Stranger");
    }

    public ValueTask DisposeAsync()
    {
        _owner.Dispose();
        _stranger.Dispose();
        _factory.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>Enough of an account to be worth losing: a class, a finished task and a fight.</summary>
    private static async Task PopulateAsync(HttpClient client)
    {
        (await client.PutAsJsonAsync("/api/rpg/class", new { classKey = "fighter" }))
            .EnsureSuccessStatusCode();

        var created = await client.PostAsJsonAsync(
            "/api/tasks", new { title = "Something worth keeping", difficulty = "epic" });

        created.EnsureSuccessStatusCode();

        var task = await created.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var id = task!["id"].ToString();

        (await client.PostAsJsonAsync($"/api/tasks/{id}/complete", new { utcOffsetMinutes = 0 }))
            .EnsureSuccessStatusCode();
    }

    private async Task<int> RowsForAsync(string subject)
    {
        await using var db = postgres.CreateContext();
        var token = TestContext.Current.CancellationToken;

        var user = await db.Users.SingleAsync(u => u.Auth0Sub == subject, token);

        return await db.Tasks.CountAsync(t => t.UserId == user.Id, token)
            + await db.AchievementUnlocks.CountAsync(a => a.UserId == user.Id, token)
            + await db.InventoryItems.CountAsync(i => i.UserId == user.Id, token)
            + await db.ChronicleEntries.CountAsync(e => e.UserId == user.Id, token)
            + await db.QuestProgress.CountAsync(q => q.UserId == user.Id, token);
    }

    [Fact]
    public async Task Resetting_deletes_everything_and_keeps_the_login()
    {
        await PopulateAsync(_owner);

        Assert.True(await RowsForAsync("auth0|owner") > 0);

        var response = await _owner.PostAsJsonAsync("/api/account/reset", new { confirm = "RESET" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await RowsForAsync("auth0|owner"));

        await using var db = postgres.CreateContext();
        var token = TestContext.Current.CancellationToken;

        // The identity survives, and with it the id everything was ever keyed by.
        var user = await db.Users.SingleAsync(u => u.Auth0Sub == "auth0|owner", token);

        Assert.Equal("owner@example.com", user.Email);

        // Back to the state a first sign-in produces, which is what puts the app on class select.
        var character = await db.Characters.SingleAsync(c => c.UserId == user.Id, token);

        Assert.Null(character.ClassKey);
        Assert.Equal(0, character.TotalXp);
        Assert.Equal(0, character.TasksCompleted);
        Assert.Equal(0, character.Gold);
        Assert.Equal(0, character.Essence);
        Assert.Equal(0, character.Ascensions);
    }

    [Fact]
    public async Task The_word_has_to_be_typed()
    {
        await PopulateAsync(_owner);
        var before = await RowsForAsync("auth0|owner");

        foreach (var body in new object[] { new { confirm = "" }, new { confirm = "reset" }, new { } })
        {
            var response = await _owner.PostAsJsonAsync("/api/account/reset", body);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal(before, await RowsForAsync("auth0|owner"));
    }

    [Fact]
    public async Task One_account_reset_leaves_another_account_untouched()
    {
        await PopulateAsync(_owner);
        await PopulateAsync(_stranger);

        var strangersRows = await RowsForAsync("auth0|stranger");

        (await _owner.PostAsJsonAsync("/api/account/reset", new { confirm = "RESET" }))
            .EnsureSuccessStatusCode();

        Assert.Equal(0, await RowsForAsync("auth0|owner"));
        Assert.Equal(strangersRows, await RowsForAsync("auth0|stranger"));
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_reset_anything()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/account/reset", new { confirm = "RESET" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
