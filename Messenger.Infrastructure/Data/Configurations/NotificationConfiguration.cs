using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        builder.HasKey(e => e.NotificationId).HasName("Notifications_pkey");
        builder.Property(e => e.NotificationId)
            .HasColumnName("Notification_ID")
            .ValueGeneratedNever();
        builder.Property(e => e.UserId).HasColumnName("User_ID");
        builder.Property(e => e.Text).IsRequired();
        builder.Property(e => e.DeduplicationKey)
            .HasMaxLength(256)
            .HasColumnName("Deduplication_Key");
        builder.Property(e => e.CreationDate)
            .HasColumnName("Creation_Date")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now() at time zone 'utc'");
        builder.Property(e => e.Read).HasDefaultValue(false);
        builder.Property(e => e.ReadAt)
            .HasColumnName("Read_At")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(e => e.DeduplicationKey)
            .IsUnique()
            .HasFilter("\"Deduplication_Key\" IS NOT NULL")
            .HasDatabaseName("IX_Notifications_Deduplication_Key");

        builder.HasIndex(e => new { e.UserId, e.Read })
            .HasDatabaseName("IX_Notifications_UserId_Read");

        builder.HasOne(d => d.User)
            .WithMany(p => p.Notifications)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired()
            .HasConstraintName("fk_notification_user");
    }
}
