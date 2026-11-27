using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class UserStatusConfiguration : IEntityTypeConfiguration<UserStatus>
{
    public void Configure(EntityTypeBuilder<UserStatus> builder)
    {
        builder.ToTable("User_Statuses");

        builder.HasKey(e => e.UserId).HasName("User_Statuses_pkey");
        builder.Property(e => e.UserId).HasColumnName("User_ID").ValueGeneratedNever();
        builder.Property(e => e.Online);
        builder.Property(e => e.LastActivity)
            .HasColumnName("Last_Activity")
            .HasColumnType("timestamp with time zone");

        builder.HasOne(d => d.User)
            .WithOne(p => p.UserStatus)
            .HasForeignKey<UserStatus>(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired()
            .HasConstraintName("fk_status_user");
    }
}
