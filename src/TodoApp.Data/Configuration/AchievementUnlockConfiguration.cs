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

        builder.Property(a => a.UserId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(a => a.AchievementKey)
            .HasColumnType("varchar(60)")
            .IsRequired();

        builder.Property(a => a.UnlockedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Uniqueness is per user per badge. A unique index on AchievementKey alone would
        // mean the first user to earn a badge permanently blocks everyone else from it.
        // This stays a database constraint because it is what makes the grant in
        // GamificationService safe under concurrency.
        builder.HasIndex(a => new { a.UserId, a.AchievementKey }).IsUnique();
    }
}
