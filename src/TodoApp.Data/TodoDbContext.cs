using Microsoft.EntityFrameworkCore;
using TodoApp.Models;

namespace TodoApp.Data;

public class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<TodoTask> Tasks => Set<TodoTask>();

    public DbSet<Character> Characters => Set<Character>();

    public DbSet<AchievementUnlock> AchievementUnlocks => Set<AchievementUnlock>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TodoDbContext).Assembly);
    }
}
