using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoApp.Models;

namespace TodoApp.Data.Configuration;

public class AchievementUnlockConfiguration : IEntityTypeConfiguration<AchievementUnlock>
{
    public void Configure(EntityTypeBuilder<AchievementUnlock> builder)
    {
        builder.ToTable("achievement_unlocks");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnType("integer")
            .ValueGeneratedOnAdd();

        builder.Property(a => a.AchievementKey)
            .HasColumnType("varchar(60)")
            .IsRequired();

        builder.Property(a => a.UnlockedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // A badge can only be earned once; the unique index is the real guard, not the code path.
        builder.HasIndex(a => a.AchievementKey).IsUnique();
    }
}
