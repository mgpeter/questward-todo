using TodoApp.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

// ------------------------------------------------------------------------ configuration
// One config file for both orchestrators. .env is gitignored (.gitignore:103), so nothing
// secret-shaped moves into the repository by reading it here.
builder.AddComposeEnvFile(Path.Combine(builder.AppHostDirectory, "..", "..", ".env"));

// None of the three are secret: a PKCE flow has no client secret and the SPA client id is
// public by design. Marking them secret would only hide them in the dashboard. Unsatisfied,
// Aspire prompts for them and stores the answer in user secrets.
var auth0Domain = builder.AddParameter("auth0-domain");
var auth0Audience = builder.AddParameter("auth0-audience");
var auth0SpaClientId = builder.AddParameter("auth0-spa-client-id");

// Pinned rather than generated, and this matters more than it looks. AddPostgres generates a
// random password when none is supplied and stores it in user secrets. Combine that with a
// data volume and a persistent container and you get a database you can only start once:
// clear user secrets, or clone onto another machine, and run two initialises a cluster with
// password A while run three connects with password B. Postgres then refuses forever, and it
// presents as "the app is broken" rather than as a credentials problem.
var postgresUser = builder.AddParameter(
    "postgres-username",
    builder.Configuration["Parameters:postgres-username"] ?? "questward");

var postgresPassword = builder.AddParameter(
    "postgres-password",
    builder.Configuration["Parameters:postgres-password"] ?? "questward",
    secret: true);

// ----------------------------------------------------------------------------- postgres
var postgres = builder.AddPostgres("postgres", postgresUser, postgresPassword)
    // Same image as both compose files, so the server the AppHost runs is the server that
    // ships. WithDataVolume derives the mount path from the major version, which is the
    // Postgres 18 /var/lib/postgresql quirk docker-compose.yml:15 documents by hand.
    .WithImageTag("18-alpine")
    // Named, and deliberately NOT questward-dev-data. Sharing a volume with
    // docker-compose.dev.yml would mean two orchestrators initialising one cluster with
    // whatever credentials each happened to have, and the loser is unrecoverable.
    .WithDataVolume("questward-aspire-data")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithPgWeb();

var database = postgres.AddDatabase("questward");

// ---------------------------------------------------------------------------------- api
var api = builder.AddProject<Projects.TodoApp_Api>("api")
    // ConnectionStrings__Default, not WithReference's ConnectionStrings__questward, so the
    // API keeps reading the key its AddDbContext lambda already reads. Program.cs, the EF
    // version pins in TodoApp.Data.csproj and QuestwardAppFactory are all left alone - which
    // is why adding Aspire does not touch a single one of the existing tests.
    .WithEnvironment("ConnectionStrings__Default", database)
    .WaitFor(database)
    .WithEnvironment("Auth0__Domain", auth0Domain)
    .WithEnvironment("Auth0__Audience", auth0Audience)
    .WithEnvironment("Auth0__SpaClientId", auth0SpaClientId)
    .WithHttpHealthCheck("/health");

// ---------------------------------------------------------------------------------- spa
// A live dev server, not a build step: this is the reason to run under an AppHost at all.
// WithNpm also installs packages before first start. Deliberately no WithReference(api) -
// the browser only ever talks to the gateway, so the SPA's relative /api calls are routed by
// YARP and Vite's own proxy is never used here.
var web = builder.AddViteApp("web", "../../web")
    .WithNpm();

// ------------------------------------------------------------------------------ gateway
// The front door, and the only resource with an externally published endpoint. Its port is
// pinned to 5080 in launchSettings.json: AuthBootstrap.tsx sends
// redirect_uri: window.location.origin, and Auth0's Allowed Callback URLs are literal
// strings, so a port Aspire picked at random would fail sign-in on every restart. 5080 is
// already on that list, and it is what all four verification scripts already default to.
builder.AddProject<Projects.TodoApp_Gateway>("gateway")
    .WithReference(api)
    .WithReference(web)
    .WaitFor(api)
    .WaitFor(web)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

builder.Build().Run();
