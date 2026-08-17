using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Models;

namespace TodoApp.Api.Services;

/// <summary>
/// Just-in-time user provisioning: the first time a subject is seen, create the user and
/// their character together, then reuse that record forever after.
/// </summary>
public sealed class UserProvisioner(TodoDbContext db)
{
    public async Task<User> GetOrCreateAsync(
        string subject,
        string? email,
        string? displayName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        var existing = await db.Users
            .FirstOrDefaultAsync(u => u.Auth0Sub == subject, cancellationToken);

        if (existing is not null)
        {
            return await RefreshAsync(existing, email, displayName, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;

        var user = new User
        {
            Auth0Sub = subject,
            Email = email,
            DisplayName = displayName,
            CreatedAt = now,
            LastSeenAt = now
        };

        // The user and their character are created together, so a user can never exist
        // without one and every downstream read can assume it is there.
        db.Users.Add(user);
        db.Characters.Add(new Character
        {
            UserId = user.Id,
            Name = FirstNameOrDefault(displayName),
            AvatarKey = "fox",
            TotalXp = 0,
            TasksCompleted = 0,
            CreatedAt = now
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return user;
        }
        catch (DbUpdateException)
        {
            // Two concurrent first requests for the same subject. The unique index on
            // Auth0Sub is the guard rather than a check-then-insert, which would race.
            // Whoever lost re-reads the winner's row.
            db.ChangeTracker.Clear();

            var winner = await db.Users
                .FirstOrDefaultAsync(u => u.Auth0Sub == subject, cancellationToken);

            if (winner is null)
            {
                throw;
            }

            return winner;
        }
    }

    private async Task<User> RefreshAsync(
        User user,
        string? email,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var changed = false;

        if (!string.IsNullOrWhiteSpace(email) && user.Email != email)
        {
            user.Email = email;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(displayName) && user.DisplayName != displayName)
        {
            user.DisplayName = displayName;
            changed = true;
        }

        // Coarse so an active session does not write on every single request.
        if (DateTimeOffset.UtcNow - user.LastSeenAt > TimeSpan.FromMinutes(5))
        {
            user.LastSeenAt = DateTimeOffset.UtcNow;
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return user;
    }

    private static string FirstNameOrDefault(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "Adventurer";
        }

        var first = displayName.Trim().Split(' ')[0];
        return first.Length > 60 ? first[..60] : first;
    }
}
