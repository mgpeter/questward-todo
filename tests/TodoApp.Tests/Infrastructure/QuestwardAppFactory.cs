using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using TodoApp.Data;

namespace TodoApp.Tests.Infrastructure;

/// <summary>
/// Boots the real API against the Testcontainers Postgres, with the Auth0 bearer scheme
/// swapped for <see cref="TestAuthHandler"/>. Everything else, including every endpoint,
/// filter and query, is the production wiring.
/// </summary>
public sealed class QuestwardAppFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
                // Satisfies the fail-fast startup validation. Never used: the bearer
                // handler is replaced below, so no token is ever validated against it.
                ["Auth0:Domain"] = "questward-tests.eu.auth0.com",
                ["Auth0:Audience"] = "https://questward.tests",
                ["Auth0:SpaClientId"] = "test-client-id"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Belt and braces. Program.cs now resolves the connection string from DI so the
            // configuration override above is sufficient, but a failure here is silent and
            // expensive: the suite would run green against the developer's own database
            // instead of the container. This registration lands after Program's and wins
            // regardless of configuration timing, so that can never happen again.
            services.RemoveAll<DbContextOptions<TodoDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<TodoDbContext>();
            services.AddDbContext<TodoDbContext>(options => options.UseNpgsql(connectionString));

            // Point the default challenge and authenticate schemes at the test handler,
            // leaving RequireAuthorization() and the whole authorization pipeline intact.
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName,
                    _ => { });

            services.RemoveAll<IConfigureOptions<JwtBearerOptions>>();

            services.PostConfigure<AuthorizationOptions>(options =>
                options.DefaultPolicy = new AuthorizationPolicyBuilder(TestAuthHandler.SchemeName)
                    .RequireAuthenticatedUser()
                    .Build());
        });
    }

    /// <summary>A client authenticated as the given subject.</summary>
    public HttpClient CreateClientAs(string subject, string? email = null, string? name = null)
    {
        var client = CreateClient();

        client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, subject);

        if (email is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        }

        if (name is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.NameHeader, name);
        }

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return client;
    }

    /// <summary>A client with no credentials at all.</summary>
    public HttpClient CreateAnonymousClient() => CreateClient();
}
