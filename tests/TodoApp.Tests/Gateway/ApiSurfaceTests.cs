using System.Net;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Gateway;

/// <summary>
/// What the API stopped being when the gateway took over the origin.
/// </summary>
/// <remarks>
/// DEC-016 moved static file serving and the single-page-app fallback out of the API. Nothing
/// in the existing suite ever requested a non-<c>/api</c> path, so that move was invisible to
/// it - which is precisely why it is worth pinning down here. Without this, the API could
/// quietly reacquire the SPA and two things would then claim the same responsibility.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class ApiSurfaceTests(PostgresFixture postgres) : IAsyncLifetime
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

    [Fact]
    public void The_api_no_longer_serves_the_single_page_app()
    {
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        var fallback = endpoints
            .Where(endpoint => endpoint.DisplayName?.StartsWith("Fallback", StringComparison.Ordinal) == true)
            .Select(endpoint => endpoint.DisplayName)
            .ToList();

        Assert.Empty(fallback);
    }

    [Fact]
    public async Task An_unknown_non_api_path_is_404_rather_than_the_app_shell()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync("/adventure");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Liveness_and_health_are_both_anonymous_and_distinct()
    {
        // /health is the API's own, unchanged since before Aspire, and asserted elsewhere.
        // /alive comes from ServiceDefaults. Two endpoints on one route would throw
        // AmbiguousMatchException at request time, so this is the guard on that collision -
        // and it has to be a request, because the failure only exists at match time.
        using var client = _factory.CreateAnonymousClient();

        var health = await client.GetAsync("/health");
        var alive = await client.GetAsync("/alive");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
    }
}
