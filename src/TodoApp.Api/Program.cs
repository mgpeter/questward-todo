using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using TodoApp.Api.Endpoints;
using TodoApp.Api.Services;
using TodoApp.Data;

const string DevCorsPolicy = "questward-dev";

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=questward;Username=questward;Password=questward";

builder.Services.AddDbContext<TodoDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddScoped<AchievementEvaluator>();
builder.Services.AddScoped<GamificationService>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

builder.Services.AddProblemDetails();
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

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithTags("System");

app.MapTaskEndpoints();
app.MapCharacterEndpoints();
app.MapAchievementEndpoints();
app.MapStatsEndpoints();

// Unmatched API routes must 404 rather than fall through to the SPA shell below.
app.MapMethods("/api/{**rest}", ["GET", "POST", "PUT", "DELETE", "PATCH"], () => Results.NotFound());

// Everything else is the single-page app (production only; in dev Vite serves it).
app.MapFallbackToFile("index.html");

app.Run();
