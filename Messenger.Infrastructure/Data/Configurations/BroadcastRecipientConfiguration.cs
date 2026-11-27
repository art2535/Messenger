using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class BroadcastRecipientConfiguration : IEntityTypeConfiguration<BroadcastRecipient>
{
    public void Configure(EntityTypeBuilder<BroadcastRecipient> builder)
    {
        builder.HasKey(e => new { e.BroadcastId, e.UserId }).HasName("Broadcast_Recipients_pkey");

        builder.Property(e => e.BroadcastId).HasColumnName("Broadcast_ID");
        builder.Property(e => e.UserId).HasColumnName("User_ID");

        builder.Property(e => e.SentAt)
            .HasColumnName("Sent_At")
            .HasDefaultValueSql("now()")
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.DeliveredAt)
            .HasColumnName("Delivered_At")
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.ReadAt)
            .HasColumnName("Read_At")
            .HasColumnType("timestamp with time zone");

        builder.HasOne(d => d.Broadcast)
            .WithMany(p => p.Recipients)
            .HasForeignKey(d => d.BroadcastId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_recipient_broadcast");

        builder.HasOne(d => d.User)
            .WithMany(p => p.BroadcastRecipients)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_recipient_user");
    }
}
