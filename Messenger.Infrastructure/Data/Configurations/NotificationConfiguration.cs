using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.Property(e => e.CreationDate).HasColumnType("timestamp with time zone");
        builder.Property(e => e.ReadAt).HasColumnType("timestamp with time zone");
        builder.HasKey(e => e.NotificationId).HasName("Notifications_pkey");
        builder.Property(e => e.NotificationId).ValueGeneratedNever();
        builder.Property(e => e.CreationDate)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now() at time zone 'utc'");
        builder.Property(e => e.Read).HasDefaultValue(false);
        builder.HasIndex(e => new { e.UserId, e.Read })
            .HasDatabaseName("IX_Notifications_UserId_Read");
        builder.HasOne(d => d.User).WithMany(p => p.Notifications)
            .HasConstraintName("fk_notification_user");
    }
}
