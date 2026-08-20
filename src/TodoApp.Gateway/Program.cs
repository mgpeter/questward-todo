var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    // Destinations are written as service names - "http://api", never a host and port. This
    // resolver turns the name into an address using the Services: configuration section,
    // which the Aspire AppHost fills in from WithReference and docker-compose.yml fills in by
    // hand. One mechanism, two sources; there is no code path that asks which one is running.
    .AddServiceDiscoveryDestinationResolver();

// Fail at startup rather than booting and answering every request with a 502. Without a
// Services: entry the resolver falls back to plain DNS on port 80, so the failure would
// surface once per request instead of once, at the moment it became true. This mirrors the
// API's Auth0 ValidateOnStart, for the same reason.
if (string.IsNullOrWhiteSpace(builder.Configuration["Services:api:http:0"]))
{
    throw new InvalidOperationException(
        "No address for the api service. Set Services__api__http__0 (docker compose), or run "
        + "under TodoApp.AppHost, which injects it.");
}

var app = builder.Build();

// The SPA is either a live Vite dev server the AppHost has told us about, or a folder of
// files baked into this image. Never both. The presence of the service discovery entry is
// what decides, not the environment name: a container running as Development would otherwise
// try to proxy to a Vite that is not there.
var viteDevServer = builder.Configuration["Services:web:http:0"];
var serveStaticSpa = string.IsNullOrWhiteSpace(viteDevServer);

if (serveStaticSpa)
{
    // Gated together with the fallback, not just alongside it. A developer who has ever run
    // `npm run build` has a populated wwwroot, and an ungated UseStaticFiles would serve that
    // stale bundle in place of Vite's live modules - silently, with no error anywhere.
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.MapDefaultEndpoints();

// The gateway's own liveness, distinct from the API's. Answers for the proxy process only:
// whether the API behind it is well is what /alive on the API is for.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapReverseProxy();

if (serveStaticSpa)
{
    // Everything the API routes and the static files did not claim is the single-page app.
    // Deep links have to reach index.html or a refresh on /adventure is a 404. Registered at
    // Order = int.MaxValue, so every proxy route outranks it by construction.
    app.MapFallbackToFile("index.html");
}

app.Run();

// Deliberately no `public partial class Program;` here, unlike the API. Both would land in
// the global namespace, and a test project referencing both would find `Program` ambiguous -
// breaking all 850 existing tests to gain a factory nothing needs. The gateway's tests read
// its configuration from disk instead.
