using System.Text.Json;
using System.Text.Json.Nodes;

namespace TodoApp.Tests.Gateway;

/// <summary>
/// The gateway's routing contract, read from the files that actually configure it.
/// </summary>
/// <remarks>
/// Reading <c>appsettings.json</c> rather than asserting against a duplicated route table is
/// the same discipline <see cref="Design.ContrastTests"/> applies to the palette: a copy here
/// would be a second source of truth that passes while the gateway ships something else.
/// <para>
/// These do not boot the gateway. Its <c>Program</c> is deliberately not made public - both
/// it and the API's would land in the global namespace and every existing test would stop
/// compiling on an ambiguous <c>Program</c>. Nothing here needs a host anyway.
/// </para>
/// </remarks>
public class GatewayContractTests
{
    private static readonly JsonDocumentOptions Lenient = new()
    {
        // Both gateway settings files carry explanatory comments, as everything else in this
        // repository does. ASP.NET Core's JSON configuration provider allows them too.
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    [Fact]
    public void Every_api_path_is_routed_to_the_api_cluster()
    {
        var route = Settings("appsettings.json")["ReverseProxy"]!["Routes"]!["api"]!;

        Assert.Equal("api", (string?)route["ClusterId"]);
        Assert.Equal("/api/{**catch-all}", (string?)route["Match"]!["Path"]);
    }

    [Fact]
    public void The_api_route_does_not_strip_its_prefix()
    {
        // The API's own routes are /api/*, and the SPA calls them on bare relative paths. A
        // PathRemovePrefix transform here would deliver /config to an app that only answers
        // /api/config, and sign-in would fail at the first fetch.
        var transforms = Settings("appsettings.json")["ReverseProxy"]!["Routes"]!["api"]!["Transforms"];

        var prefixTransforms = transforms?.AsArray()
            .Where(t => t?["PathRemovePrefix"] is not null)
            .ToList();

        Assert.Empty(prefixTransforms ?? []);
    }

    [Fact]
    public void The_api_destination_is_a_service_name_rather_than_an_address()
    {
        // "http://api" is resolved from the Services: section by service discovery, which the
        // AppHost fills in via WithReference and docker-compose.yml fills in by hand. A literal
        // host and port here would work under exactly one of the two.
        var destinations = Settings("appsettings.json")["ReverseProxy"]!["Clusters"]!["api"]!["Destinations"]!;

        Assert.Equal("http://api", (string?)destinations["api"]!["Address"]);
    }

    [Fact]
    public void The_development_spa_route_cannot_capture_api_paths()
    {
        // The SPA route is a catch-all, so it matches /api too. Ordering is what keeps the API
        // reachable, and an explicit Order wins outright rather than relying on YARP's
        // precedence rules staying what they are today.
        var apiOrder = (int?)Settings("appsettings.json")["ReverseProxy"]!["Routes"]!["api"]!["Order"];
        var development = Settings("appsettings.Development.json")["ReverseProxy"]!["Routes"]!;

        var spaOrder = (int?)development["spa"]!["Order"];

        Assert.NotNull(apiOrder);
        Assert.NotNull(spaOrder);
        Assert.True(
            spaOrder > apiOrder,
            $"The SPA catch-all (Order {spaOrder}) must sort after the API route (Order {apiOrder}).");
    }

    [Fact]
    public void The_browsable_api_reference_survives_being_put_behind_the_gateway()
    {
        // /scalar and /openapi sit outside /api, so they are not covered by the API route and
        // would 404 once the gateway owned the development origin. README documents them.
        var routes = Settings("appsettings.Development.json")["ReverseProxy"]!["Routes"]!;

        Assert.Equal("api", (string?)routes["scalar"]!["ClusterId"]);
        Assert.Equal("api", (string?)routes["openapi"]!["ClusterId"]);
    }

    [Fact]
    public void The_api_container_publishes_no_host_port()
    {
        // Load-bearing, not tidiness. The API runs with ASPNETCORE_FORWARDEDHEADERS_ENABLED,
        // which clears the proxy allow-list and makes it trust X-Forwarded-For from anything
        // that can reach it. That is safe only while the gateway is the only thing that can:
        // otherwise the per-address rate limit on /api/config is spoofable by anyone.
        var compose = File.ReadAllLines(RepoFile("docker-compose.yml"));

        var api = ServiceBlock(compose, "api");

        Assert.DoesNotContain(api, line => line.TrimStart().StartsWith("ports:", StringComparison.Ordinal));
    }

    [Fact]
    public void The_gateway_container_is_the_one_that_publishes_a_port()
    {
        var compose = File.ReadAllLines(RepoFile("docker-compose.yml"));

        var gateway = ServiceBlock(compose, "gateway");

        Assert.Contains(gateway, line => line.TrimStart().StartsWith("ports:", StringComparison.Ordinal));
    }

    /// <summary>The lines of one top-level service in docker-compose.yml, excluding its key.</summary>
    private static List<string> ServiceBlock(string[] lines, string service)
    {
        var start = Array.FindIndex(lines, line => line == $"  {service}:");

        Assert.True(start >= 0, $"No service named '{service}' in docker-compose.yml.");

        var block = new List<string>();

        for (var i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i];

            // A non-blank line at two-space indentation starts the next service.
            if (line.Length > 0 && !line.StartsWith("    ", StringComparison.Ordinal) && line.Trim().Length > 0)
            {
                break;
            }

            block.Add(line);
        }

        return block;
    }

    private static JsonNode Settings(string fileName)
    {
        var path = RepoFile(Path.Combine("src", "TodoApp.Gateway", fileName));

        return JsonNode.Parse(File.ReadAllText(path), documentOptions: Lenient)!;
    }

    private static string RepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find {relativePath} by walking up from {AppContext.BaseDirectory}");
    }
}
