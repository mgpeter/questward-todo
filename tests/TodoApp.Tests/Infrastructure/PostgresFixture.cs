using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TodoApp.Data;
using TodoApp.Models;

namespace TodoApp.Tests.Infrastructure;

/// <summary>
/// A real Postgres for the whole test run. Per-user scoping is enforced in SQL by indexes
/// and foreign keys, so an in-memory provider would not exercise what makes it correct.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        // Same major version as production, so constraint behaviour matches.
        .WithImage("postgres:18-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public TodoDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TodoDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

    /// <summary>Empties every table so each test starts from a known state.</summary>
    public async Task ResetAsync()
    {
        await using var db = CreateContext();

        // Cascades through tasks, characters and unlocks via their foreign keys.
        await db.Database.ExecuteSqlRawAsync("TRUNCATE users CASCADE;");
    }

    public async Task<User> CreateUserAsync(string subject)
    {
        await using var db = CreateContext();

        var user = new User { Auth0Sub = subject };

        db.Users.Add(user);
        db.Characters.Add(new Character { UserId = user.Id });
        await db.SaveChangesAsync();

        return user;
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
