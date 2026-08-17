using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TodoApp.Models;

namespace TodoApp.Data;

/// <summary>
/// Applies migrations and guarantees the singleton character row exists.
/// Retries because in Docker the app container regularly wins the race against Postgres,
/// even with a compose healthcheck in front of it.
/// </summary>
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
                break;
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

        await EnsureCharacterAsync(db, cancellationToken);
    }

    private static async Task EnsureCharacterAsync(TodoDbContext db, CancellationToken cancellationToken)
    {
        var exists = await db.Characters
            .AnyAsync(c => c.Id == Character.SingletonId, cancellationToken);

        if (exists)
        {
            return;
        }

        db.Characters.Add(new Character
        {
            Id = Character.SingletonId,
            Name = "Adventurer",
            AvatarKey = "fox",
            TotalXp = 0,
            TasksCompleted = 0,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
