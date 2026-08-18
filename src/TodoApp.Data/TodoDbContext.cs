using Microsoft.EntityFrameworkCore;
using TodoApp.Models;
using TodoApp.Models.Rpg;

namespace TodoApp.Data;

public class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<TodoTask> Tasks => Set<TodoTask>();

    public DbSet<Character> Characters => Set<Character>();

    public DbSet<AchievementUnlock> AchievementUnlocks => Set<AchievementUnlock>();

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    public DbSet<ShopPurchase> ShopPurchases => Set<ShopPurchase>();

    public DbSet<Encounter> Encounters => Set<Encounter>();

    public DbSet<QuestProgress> QuestProgress => Set<QuestProgress>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TodoDbContext).Assembly);
    }
}
