using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class BroadcastConfiguration : IEntityTypeConfiguration<Broadcast>
{
    public void Configure(EntityTypeBuilder<Broadcast> builder)
    {
        builder.ToTable("Broadcasts");

        builder.HasKey(e => e.BroadcastId).HasName("Broadcasts_pkey");
        builder.Property(e => e.BroadcastId)
            .HasColumnName("Broadcast_ID")
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.Title).HasColumnName("Title").HasMaxLength(200).IsRequired();
        builder.Property(e => e.MessageText).HasColumnName("Message_Text").IsRequired();
        builder.Property(e => e.SenderId).HasColumnName("Sender_ID");
        builder.Property(e => e.CreatedAt)
            .HasColumnName("Created_At")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");
        builder.Property(e => e.TotalRecipients).HasColumnName("Total_Recipients");

        builder.HasOne(d => d.Sender)
            .WithMany(p => p.BroadcastsCreated)
            .HasForeignKey(d => d.SenderId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired()
            .HasConstraintName("fk_broadcast_sender");
    }
}
