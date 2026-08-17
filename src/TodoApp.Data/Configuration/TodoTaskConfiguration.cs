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

        builder.Property(t => t.Status)
            .HasColumnType("integer")
            .IsRequired();

        // Convenience over Status, deliberately not stored: two copies of one fact drift.
        builder.Ignore(t => t.IsCompleted);
        builder.Ignore(t => t.IsProgressionBearing);

        builder.Property(t => t.ParentId).HasColumnType("uuid");

        // Postgres text[], so tag filtering stays one query rather than a join table.
        builder.Property(t => t.Tags)
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(t => t.StartedAt).HasColumnType("timestamp with time zone");

        builder.Property(t => t.Recurrence).HasColumnType("integer").IsRequired();

        builder.Property(t => t.XpEligibleFrom).HasColumnType("timestamp with time zone");

        builder.Property(t => t.CompletedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(t => t.XpAwarded)
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(t => t.StaminaAwarded)
            .HasColumnType("integer")
            .IsRequired()
            .HasDefaultValue(0);

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

        // A subtask is a task, so the parent link is a self-reference. Cascade, because a
        // subtask has no meaning once its parent is gone.
        builder.HasOne<TodoTask>()
            .WithMany()
            .HasForeignKey(t => t.ParentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Both indexes lead with UserId because no query reads across users. Keeping the
        // old user-less indexes would be dead weight maintained on every write.
        builder.HasIndex(t => new { t.UserId, t.Status, t.SortOrder });
        builder.HasIndex(t => new { t.UserId, t.CompletedAt });

        // Fetching a parent's children is the hot path for the list view.
        builder.HasIndex(t => t.ParentId);

        // GIN, because tag filtering is a containment query and btree cannot serve it.
        builder.HasIndex(t => t.Tags).HasMethod("gin");
    }
}
