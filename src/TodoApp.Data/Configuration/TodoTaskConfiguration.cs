using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoApp.Models;

namespace TodoApp.Data.Configuration;

public class TodoTaskConfiguration : IEntityTypeConfiguration<TodoTask>
{
    public void Configure(EntityTypeBuilder<TodoTask> builder)
    {
        builder.ToTable("tasks");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(t => t.UserId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(t => t.Title)
            .HasColumnType("varchar(200)")
            .IsRequired();

        builder.Property(t => t.Notes)
            .HasColumnType("text");

        builder.Property(t => t.Difficulty)
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(t => t.Priority)
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(t => t.DueDate)
            .HasColumnType("timestamp with time zone");

        builder.Property(t => t.IsCompleted)
            .HasColumnType("boolean")
            .IsRequired();

        builder.Property(t => t.CompletedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(t => t.XpAwarded)
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(t => t.SortOrder)
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Both indexes lead with UserId because no query reads across users. Keeping the
        // old user-less indexes would be dead weight maintained on every write.
        builder.HasIndex(t => new { t.UserId, t.IsCompleted, t.SortOrder });
        builder.HasIndex(t => new { t.UserId, t.CompletedAt });
    }
}
