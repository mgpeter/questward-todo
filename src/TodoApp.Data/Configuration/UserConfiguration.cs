using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoApp.Models;

namespace TodoApp.Data.Configuration;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id)
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        // Provider-prefixed and undocumented in length; 128 is generous headroom.
        builder.Property(u => u.Auth0Sub)
            .HasColumnType("varchar(128)")
            .IsRequired();

        // Maximum length of an addr-spec. Nullable because not every connection returns it.
        builder.Property(u => u.Email)
            .HasColumnType("varchar(320)");

        builder.Property(u => u.DisplayName)
            .HasColumnType("varchar(200)");

        builder.Property(u => u.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(u => u.LastSeenAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // Not a convenience: this is the concurrency guard for just-in-time provisioning.
        // Two simultaneous first requests must not create two rows for one subject.
        builder.HasIndex(u => u.Auth0Sub).IsUnique();
    }
}
