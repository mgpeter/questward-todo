using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoApp.Models;

namespace TodoApp.Data.Configuration;

public class CharacterConfiguration : IEntityTypeConfiguration<Character>
{
    public void Configure(EntityTypeBuilder<Character> builder)
    {
        builder.ToTable("character", table => table.HasCheckConstraint(
            "ck_character_singleton",
            $"\"Id\" = {Character.SingletonId}"));

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnType("integer")
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
    }
}
