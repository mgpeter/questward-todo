using Microsoft.Extensions.Configuration;

namespace TodoApp.AppHost;

/// <summary>
/// Reads the compose stack's gitignored <c>.env</c> into the AppHost's own configuration.
/// </summary>
/// <remarks>
/// Deliberately not a general dotenv parser. It maps a fixed set of keys onto the
/// <c>Parameters:</c> section that <c>AddParameter</c> reads, so <c>aspire run</c> and
/// <c>docker compose up</c> are configured by one file rather than two that quietly disagree
/// about which Auth0 tenant this machine uses.
///
/// An allow-list rather than a splat: <c>QUESTWARD_PORT</c> is compose's business and has no
/// place in the Aspire graph. Only present, non-empty values are added, and they are added
/// last, so <c>.env</c> wins where it speaks and user secrets answer for everything else. A
/// missing <c>.env</c> is not an error - Aspire prompts for an unsatisfied parameter.
/// </remarks>
internal static class DotEnv
{
    /// <summary>Environment variable name in .env -> Aspire parameter name.</summary>
    private static readonly Dictionary<string, string> ParameterNames = new(StringComparer.Ordinal)
    {
        ["AUTH0_DOMAIN"] = "auth0-domain",
        ["AUTH0_AUDIENCE"] = "auth0-audience",
        ["AUTH0_SPA_CLIENT_ID"] = "auth0-spa-client-id",
        ["POSTGRES_USER"] = "postgres-username",
        ["POSTGRES_PASSWORD"] = "postgres-password",
    };

    public static IDistributedApplicationBuilder AddComposeEnvFile(
        this IDistributedApplicationBuilder builder,
        string path)
    {
        if (!File.Exists(path))
        {
            return builder;
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            var separator = trimmed.IndexOf('=');

            if (separator <= 0)
            {
                continue;
            }

            var key = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim().Trim('"');

            if (value.Length == 0 || !ParameterNames.TryGetValue(key, out var parameter))
            {
                continue;
            }

            values[$"Parameters:{parameter}"] = value;
        }

        if (values.Count > 0)
        {
            builder.Configuration.AddInMemoryCollection(values);
        }

        return builder;
    }
}
