using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoApp.Models;
using TodoApp.Models.Rpg;

namespace TodoApp.Data.Configuration;

public class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("inventory_items");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id).HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(i => i.UserId).HasColumnType("uuid").IsRequired();
        builder.Property(i => i.ItemKey).HasColumnType("varchar(60)").IsRequired();
        builder.Property(i => i.Rarity).HasColumnType("integer").IsRequired();
        builder.Property(i => i.Slot).HasColumnType("integer").IsRequired();
        builder.Property(i => i.IsEquipped).HasColumnType("boolean").IsRequired();
        builder.Property(i => i.AcquiredAt).HasColumnType("timestamp with time zone").IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => i.UserId);

        // At most one equipped item per slot per user. A partial unique index rather than
        // a check in the service, for the same reason the badge index is one: application
        // logic loses the race, the database does not.
        builder.HasIndex(i => new { i.UserId, i.Slot })
            .IsUnique()
            .HasFilter("\"IsEquipped\"");
    }
}

public class EncounterConfiguration : IEntityTypeConfiguration<Encounter>
{
    public void Configure(EntityTypeBuilder<Encounter> builder)
    {
        builder.ToTable("encounters");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(e => e.UserId).HasColumnType("uuid").IsRequired();
        builder.Property(e => e.MonsterKey).HasColumnType("varchar(60)").IsRequired();
        builder.Property(e => e.MonsterHitPoints).HasColumnType("integer").IsRequired();
        builder.Property(e => e.Status).HasColumnType("integer").IsRequired();
        builder.Property(e => e.Round).HasColumnType("integer").IsRequired();
        builder.Property(e => e.Log).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.GoldAwarded).HasColumnType("integer").IsRequired();
        builder.Property(e => e.BlessingUsed).HasColumnType("boolean").IsRequired();
        builder.Property(e => e.AbilityUses).HasColumnType("jsonb").IsRequired().HasDefaultValue("{}");
        builder.Property(e => e.MonsterDisadvantageRounds).HasColumnType("integer").IsRequired().HasDefaultValue(0);
        builder.Property(e => e.StartedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(e => e.EndedAt).HasColumnType("timestamp with time zone");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.UserId, e.StartedAt });

        // One fight at a time. Without this, two concurrent requests could each spend one
        // stamina and open a second encounter, turning one unit of real work into two sets
        // of loot.
        builder.HasIndex(e => e.UserId)
            .IsUnique()
            .HasFilter($"\"Status\" = {(int)EncounterStatus.Active}");
    }
}

public class QuestProgressConfiguration : IEntityTypeConfiguration<QuestProgress>
{
    public void Configure(EntityTypeBuilder<QuestProgress> builder)
    {
        builder.ToTable("quest_progress");

        builder.HasKey(q => q.Id);

        builder.Property(q => q.Id).HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(q => q.UserId).HasColumnType("uuid").IsRequired();
        builder.Property(q => q.QuestKey).HasColumnType("varchar(60)").IsRequired();
        builder.Property(q => q.Counters).HasColumnType("jsonb").IsRequired();
        builder.Property(q => q.ClaimedAt).HasColumnType("timestamp with time zone");
        builder.Property(q => q.StartedAt).HasColumnType("timestamp with time zone").IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(q => q.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // One row per user per quest, so progress cannot be double-counted into two rows.
        builder.HasIndex(q => new { q.UserId, q.QuestKey }).IsUnique();
    }
}
