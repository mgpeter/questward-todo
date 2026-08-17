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

        builder.HasOne<User>()
            .WithOne()
            .HasForeignKey<Character>(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
