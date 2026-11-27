using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");

        builder.HasKey(e => e.MessageId).HasName("Messages_pkey");
        builder.Property(e => e.MessageId).HasColumnName("Message_ID").ValueGeneratedNever();
        builder.Property(e => e.SenderId).HasColumnName("Sender_ID");
        builder.Property(e => e.ChatId).HasColumnName("Chat_ID");
        builder.Property(e => e.MessageText).HasColumnName("Message_Text").IsRequired();
        builder.Property(e => e.HasAttachments).HasColumnName("Has_Attachments");
        builder.Property(e => e.SendTime).HasColumnName("Send_Time").HasColumnType("timestamp with time zone");
        builder.Property(e => e.SequenceNumber).HasColumnName("Sequence_Number");
        builder.Property(e => e.DeliveryStatus).HasColumnName("Delivery_Status");
        builder.Property(e => e.ReadTime).HasColumnName("Read_Time").HasColumnType("timestamp with time zone");
        builder.Property(e => e.IsPinned).HasColumnName("Is_Pinned").HasDefaultValue(false);
        builder.Property(e => e.PinnedAt).HasColumnName("Pinned_At").HasColumnType("timestamp with time zone");

        builder.HasIndex(e => new { e.ChatId, e.SequenceNumber })
            .IsUnique().HasDatabaseName("IX_Messages_ChatId_SequenceNumber");
        builder.HasIndex(e => new { e.ChatId, e.SendTime })
            .HasDatabaseName("IX_Messages_ChatId_SendTime");
        builder.HasIndex(e => new { e.ChatId, e.ReadTime })
            .HasDatabaseName("IX_Messages_ChatId_ReadTime");
        builder.HasIndex(e => new { e.ChatId, e.IsPinned })
            .HasDatabaseName("IX_Messages_ChatId_IsPinned")
            .HasFilter("\"Is_Pinned\" = true");

        builder.HasOne(d => d.Chat)
            .WithMany(p => p.Messages)
            .HasForeignKey(d => d.ChatId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired()
            .HasConstraintName("fk_chat_messages");

        builder.HasOne(d => d.Sender)
            .WithMany(p => p.MessageSenders)
            .HasForeignKey(d => d.SenderId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired()
            .HasConstraintName("fk_sender");
    }
}
