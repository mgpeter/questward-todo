using Microsoft.EntityFrameworkCore;
using TodoApp.Models;
using TodoApp.Models.Progression;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Schema;

/// <summary>
/// The database is the guard for the ownership rules, not the application code. These
/// tests assert the constraints themselves, so a future change to a configuration class
/// that quietly drops one is caught here rather than in production.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class OwnershipSchemaTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Two_users_can_each_earn_the_same_badge()
    {
        // The regression this exists for: a unique index on AchievementKey alone would
        // mean the first user to earn First Blood permanently blocks everyone else.
        await postgres.ResetAsync();

        var alice = await postgres.CreateUserAsync("test|alice");
        var bob = await postgres.CreateUserAsync("test|bob");

        await using var db = postgres.CreateContext();

        db.AchievementUnlocks.Add(new AchievementUnlock
        {
            UserId = alice.Id,
            AchievementKey = AchievementCatalog.FirstBlood
        });
        db.AchievementUnlocks.Add(new AchievementUnlock
        {
            UserId = bob.Id,
            AchievementKey = AchievementCatalog.FirstBlood
        });

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.AchievementUnlocks
            .CountAsync(a => a.AchievementKey == AchievementCatalog.FirstBlood));
    }

    [Fact]
    public async Task One_user_cannot_earn_the_same_badge_twice()
    {
        await postgres.ResetAsync();

        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();

        db.AchievementUnlocks.Add(new AchievementUnlock
        {
            UserId = alice.Id,
            AchievementKey = AchievementCatalog.EpicSlayer
        });
        await db.SaveChangesAsync();

        db.AchievementUnlocks.Add(new AchievementUnlock
        {
            UserId = alice.Id,
            AchievementKey = AchievementCatalog.EpicSlayer
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_user_cannot_have_two_characters()
    {
        // UserId is the primary key of characters rather than a surrogate, so this is
        // impossible to represent rather than merely discouraged.
        await postgres.ResetAsync();

        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();
        db.Characters.Add(new Character { UserId = alice.Id, Name = "Impostor" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Two_users_cannot_share_an_Auth0_subject()
    {
        // This unique index is what makes just-in-time provisioning safe under
        // concurrency; without it two simultaneous first requests create two users.
        await postgres.ResetAsync();

        await postgres.CreateUserAsync("test|duplicate");

        await using var db = postgres.CreateContext();
        db.Users.Add(new User { Auth0Sub = "test|duplicate" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_task_cannot_exist_without_an_owner()
    {
        await postgres.ResetAsync();

        await using var db = postgres.CreateContext();
        db.Tasks.Add(new TodoTask { UserId = Guid.NewGuid(), Title = "Orphan" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Deleting_a_user_removes_everything_they_owned()
    {
        await postgres.ResetAsync();

        var alice = await postgres.CreateUserAsync("test|alice");
        var bob = await postgres.CreateUserAsync("test|bob");

        await using (var seed = postgres.CreateContext())
        {
            seed.Tasks.Add(new TodoTask { UserId = alice.Id, Title = "Alice's task" });
            seed.Tasks.Add(new TodoTask { UserId = bob.Id, Title = "Bob's task" });
            seed.AchievementUnlocks.Add(new AchievementUnlock
            {
                UserId = alice.Id,
                AchievementKey = AchievementCatalog.FirstBlood
            });
            await seed.SaveChangesAsync();
        }

        await using var db = postgres.CreateContext();

        db.Users.Remove(await db.Users.SingleAsync(u => u.Id == alice.Id));
        await db.SaveChangesAsync();

        Assert.Empty(await db.Tasks.Where(t => t.UserId == alice.Id).ToListAsync());
        Assert.Empty(await db.AchievementUnlocks.Where(a => a.UserId == alice.Id).ToListAsync());
        Assert.Null(await db.Characters.FirstOrDefaultAsync(c => c.UserId == alice.Id));

        // Bob is untouched.
        Assert.Single(await db.Tasks.Where(t => t.UserId == bob.Id).ToListAsync());
        Assert.NotNull(await db.Characters.FirstOrDefaultAsync(c => c.UserId == bob.Id));
    }

    [Fact]
    public async Task The_singleton_character_constraint_is_gone()
    {
        // The old schema pinned character.Id to 1 with a check constraint. If that
        // survived the migration, the second user on an instance could not be created.
        await postgres.ResetAsync();

        await postgres.CreateUserAsync("test|first");
        await postgres.CreateUserAsync("test|second");

        await using var db = postgres.CreateContext();

        Assert.Equal(2, await db.Characters.CountAsync());
    }
}
