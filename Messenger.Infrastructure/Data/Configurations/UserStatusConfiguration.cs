using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class UserStatusConfiguration : IEntityTypeConfiguration<UserStatus>
{
    public void Configure(EntityTypeBuilder<UserStatus> builder)
    {
        builder.Property(e => e.LastActivity).HasColumnType("timestamp with time zone");
        builder.HasKey(e => e.UserId).HasName("User_Statuses_pkey");
        builder.Property(e => e.UserId).ValueGeneratedNever();

        builder.HasOne(d => d.User)
            .WithOne(p => p.UserStatus)
            .HasConstraintName("fk_status_user");
    }
}
