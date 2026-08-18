using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using TodoApp.Api.Auth;
using TodoApp.Api.Endpoints;
using TodoApp.Api.Infrastructure;
using TodoApp.Api.Services;
using TodoApp.Api.Services.Rpg;
using TodoApp.Data;
using TodoApp.Models.Dice;

const string DevCorsPolicy = "questward-dev";

var builder = WebApplication.CreateBuilder(args);

// Local developer overrides, gitignored. Layered last so it wins over appsettings.json.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Resolved from DI rather than read into a local here, so it reflects the fully layered
// configuration. Reading it before builder.Build() captures whatever appsettings said and
// silently ignores anything added later, which is exactly how an integration test ends up
// pointed at the developer's own database.
builder.Services.AddDbContext<TodoDbContext>((serviceProvider, options) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();

    options.UseNpgsql(
        configuration.GetConnectionString("Default")
        ?? "Host=localhost;Port=5432;Database=questward;Username=questward;Password=questward");
});

builder.Services.AddScoped<AchievementEvaluator>();
builder.Services.AddScoped<GamificationService>();
builder.Services.AddScoped<UserProvisioner>();

// The RPG layer. The dice roller is the only source of randomness in the domain and is
// injected so the combat rules stay testable.
builder.Services.AddSingleton<IDiceRoller, SecureDiceRoller>();
builder.Services.AddScoped<CharacterSheetService>();
builder.Services.AddScoped<LootService>();
builder.Services.AddScoped<QuestService>();
builder.Services.AddScoped<BestiaryService>();
builder.Services.AddScoped<CombatService>();
// Scoped alongside CombatService on purpose: both resolve the same TodoDbContext, so the run a
// room's fight ends is the very instance this service is holding, and the two commit together.
builder.Services.AddScoped<DungeonService>();
builder.Services.AddScoped<AdventurerService>();
builder.Services.AddScoped<ShopService>();
builder.Services.AddScoped<ForgeService>();

// Fail at startup rather than booting and rejecting every request with an opaque 401.
builder.Services.AddOptions<Auth0Options>()
    .Bind(builder.Configuration.GetSection(Auth0Options.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        auth0 => !auth0.Domain.Contains("://", StringComparison.Ordinal),
        "Auth0:Domain must be the bare tenant domain (questward.eu.auth0.com), not a URL.")
    .ValidateOnStart();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, Auth0CurrentUser>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var auth0 = builder.Configuration.GetSection(Auth0Options.SectionName).Get<Auth0Options>()
            ?? new Auth0Options();

        options.Authority = auth0.Authority;
        options.Audience = auth0.Audience;

        // Keep OIDC claim names ("sub", "email") instead of having them rewritten to the
        // legacy ClaimTypes.* URIs, so Auth0CurrentUser reads what the token actually says.
        options.MapInboundClaims = false;

        // Every one of these stays on, including in Development. Signing keys are fetched
        // from the tenant's JWKS endpoint and cached by the handler.
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = auth0.Authority,
            ValidateAudience = true,
            ValidAudience = auth0.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            NameClaimType = "sub",
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

// Partial mitigation for open sign-up: it bounds what one account can do to the host.
// It does nothing to prevent account creation - see the spec's Security Posture section.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Partitioned on the token subject rather than the resolved UserId, so limiting
    // costs no database round trip.
    options.AddPolicy(RateLimitPolicies.PerUser, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.FindFirst("sub")?.Value ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1)
            }));

    // /api/config is anonymous, so it partitions by address or it becomes a free
    // amplification target.
    options.AddPolicy(RateLimitPolicies.PerAddress, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1)
            }));
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

builder.Services.AddProblemDetails();

// Registered ahead of the default handler, so a lost write is a 409 the client can retry
// rather than a 500 that reads as the server having broken.
builder.Services.AddExceptionHandler<ConcurrencyExceptionHandler>();

builder.Services.AddOpenApi();

if (builder.Environment.IsDevelopment())
{
    // Only needed while Vite serves the SPA from its own origin.
    builder.Services.AddCors(options => options.AddPolicy(DevCorsPolicy, policy => policy
        .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()));
}

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("TodoApp.Startup");

    await DatabaseInitializer.InitializeAsync(db, logger);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseCors(DevCorsPolicy);
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .AllowAnonymous()
    .WithTags("System");

app.MapSystemEndpoints();
app.MapTaskEndpoints();
app.MapCharacterEndpoints();
app.MapAchievementEndpoints();
app.MapStatsEndpoints();
app.MapRpgEndpoints();

// Unmatched API routes must 404 rather than fall through to the SPA shell below.
// Deliberately anonymous: if this required authorization, an unauthenticated request to a
// route that does not exist would return 401 and tell an anonymous caller the difference
// between a real endpoint and a typo.
app.MapMethods("/api/{**rest}", ["GET", "POST", "PUT", "DELETE", "PATCH"], () => Results.NotFound())
    .AllowAnonymous();

// Everything else is the single-page app (production only; in dev Vite serves it).
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>
/// Top-level statements compile to an internal Program class, which
/// <c>WebApplicationFactory&lt;Program&gt;</c> cannot see. This makes it visible to the
/// test project.
/// </summary>
public partial class Program;
