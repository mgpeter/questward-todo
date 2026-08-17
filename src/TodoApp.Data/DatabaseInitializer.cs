using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace TodoApp.Data;

/// <summary>
/// Applies migrations at startup. Retries because in Docker the app container regularly
/// wins the race against Postgres, even with a compose healthcheck in front of it.
/// </summary>
/// <remarks>
/// This no longer seeds a character. Characters belong to users, and users are
/// provisioned just in time on their first authenticated request.
/// </remarks>
public static class DatabaseInitializer
{
    private const int MaxAttempts = 12;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public static async Task InitializeAsync(
        TodoDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                logger.LogWarning(
                    ex,
                    "Database not ready (attempt {Attempt}/{MaxAttempts}), retrying in {Delay}s",
                    attempt,
                    MaxAttempts,
                    RetryDelay.TotalSeconds);

                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }
}
