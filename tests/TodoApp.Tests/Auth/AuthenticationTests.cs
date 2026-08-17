using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Auth;

[Collection(nameof(PostgresCollection))]
public class AuthenticationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private QuestwardAppFactory _factory = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        _factory = new QuestwardAppFactory(postgres.ConnectionString);
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData("/api/tasks")]
    [InlineData("/api/character")]
    [InlineData("/api/achievements")]
    [InlineData("/api/stats")]
    [InlineData("/api/me")]
    public async Task Protected_routes_reject_an_anonymous_caller(string route)
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/api/config")]
    public async Task Public_routes_are_reachable_without_credentials(string route)
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_api_route_is_404_even_when_anonymous()
    {
        // If the catch-all required authorization it would answer 401, telling an
        // anonymous caller the difference between a real endpoint and a typo.
        using var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Config_serves_the_public_Auth0_settings_and_nothing_else()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync("/api/config");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("questward-tests.eu.auth0.com", body);
        Assert.Contains("test-client-id", body);

        // A PKCE flow has no client secret; nothing resembling one may ever appear here.
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionString", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task First_authenticated_request_provisions_a_user_and_a_character()
    {
        using var client = _factory.CreateClientAs("auth0|alice", "alice@example.com", "Alice Example");

        var me = await client.GetFromJsonAsync<MeResponse>("/api/me");

        Assert.NotNull(me);
        Assert.NotEqual(Guid.Empty, me.Id);
        Assert.Equal("alice@example.com", me.Email);

        await using var db = postgres.CreateContext();

        var user = await db.Users.SingleAsync(u => u.Auth0Sub == "auth0|alice");
        Assert.Equal("Alice Example", user.DisplayName);

        // A user must never exist without a character; provisioning creates both together.
        Assert.NotNull(await db.Characters.SingleOrDefaultAsync(c => c.UserId == user.Id));
    }

    [Fact]
    public async Task Repeated_requests_reuse_the_same_user()
    {
        using var client = _factory.CreateClientAs("auth0|alice");

        var first = await client.GetFromJsonAsync<MeResponse>("/api/me");
        var second = await client.GetFromJsonAsync<MeResponse>("/api/me");

        Assert.Equal(first!.Id, second!.Id);

        await using var db = postgres.CreateContext();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Auth0Sub == "auth0|alice"));
    }

    [Fact]
    public async Task Different_subjects_get_different_users()
    {
        using var alice = _factory.CreateClientAs("auth0|alice");
        using var bob = _factory.CreateClientAs("auth0|bob");

        var aliceMe = await alice.GetFromJsonAsync<MeResponse>("/api/me");
        var bobMe = await bob.GetFromJsonAsync<MeResponse>("/api/me");

        Assert.NotEqual(aliceMe!.Id, bobMe!.Id);

        await using var db = postgres.CreateContext();
        Assert.Equal(2, await db.Users.CountAsync());
        Assert.Equal(2, await db.Characters.CountAsync());
    }

    [Fact]
    public async Task Concurrent_first_requests_create_only_one_user()
    {
        // The unique index on Auth0Sub is the guard, not a check-then-insert. Without it
        // a user signing in from two tabs at once would end up with two profiles.
        var clients = Enumerable.Range(0, 8)
            .Select(_ => _factory.CreateClientAs("auth0|racer"))
            .ToList();

        try
        {
            var responses = await Task.WhenAll(clients.Select(c => c.GetAsync("/api/me")));

            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

            await using var db = postgres.CreateContext();
            Assert.Equal(1, await db.Users.CountAsync(u => u.Auth0Sub == "auth0|racer"));
            Assert.Equal(1, await db.Characters.CountAsync());
        }
        finally
        {
            clients.ForEach(c => c.Dispose());
        }
    }

    private sealed record MeResponse(Guid Id, string? Email, string? DisplayName, DateTimeOffset CreatedAt);
}
