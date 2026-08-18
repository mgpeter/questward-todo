using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoApp.Models;

namespace TodoApp.Data.Configuration;

public class CharacterConfiguration : IEntityTypeConfiguration<Character>
{
    public void Configure(EntityTypeBuilder<Character> builder)
    {
        builder.ToTable("characters");

        // UserId is the key rather than a surrogate, so "exactly one character per user"
        // is enforced by the schema and cannot be broken by a bug in provisioning.
        builder.HasKey(c => c.UserId);

        builder.Property(c => c.UserId)
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(c => c.Name)
            .HasColumnType("varchar(60)")
            .IsRequired();

        builder.Property(c => c.AvatarKey)
            .HasColumnType("varchar(40)")
            .IsRequired();

        builder.Property(c => c.TotalXp)
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(c => c.TasksCompleted)
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // --- RPG layer -------------------------------------------------------
        builder.Property(c => c.ClassKey).HasColumnType("varchar(40)");

        foreach (var ability in new[]
                 {
                     nameof(Character.Strength), nameof(Character.Dexterity),
                     nameof(Character.Constitution), nameof(Character.Intelligence),
                     nameof(Character.Wisdom), nameof(Character.Charisma)
                 })
        {
            builder.Property<int>(ability).HasColumnType("integer").IsRequired().HasDefaultValue(10);
        }

        builder.Property(c => c.CurrentHitPoints).HasColumnType("integer").IsRequired().HasDefaultValue(0);
        builder.Property(c => c.Stamina).HasColumnType("integer").IsRequired().HasDefaultValue(0);
        builder.Property(c => c.Gold).HasColumnType("integer").IsRequired().HasDefaultValue(0);

        // A balance, not a computation: salvage destroys the item that paid for it, so there
        // is no surviving state to recompute Essence from. Same argument as Gold above.
        builder.Property(c => c.Essence).HasColumnType("integer").IsRequired().HasDefaultValue(0);

        builder.Property(c => c.HitPointsUpdatedAt).HasColumnType("timestamp with time zone");

        // Every balance above is read, changed in memory and written back, which without a
        // token is a lost update: two requests both read 30 essence, both pass the "can you
        // afford it" check, and both write the same absolute value, so one craft is free.
        // xmin is Postgres' own row version rather than a column of ours, so this guards Gold,
        // Stamina, hit points and essence together and no migration writes anything.
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Computed from class, level and Constitution, never stored (DEC-002).
        builder.Ignore(c => c.AbilityScores);

        builder.HasOne<User>()
            .WithOne()
            .HasForeignKey<Character>(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
