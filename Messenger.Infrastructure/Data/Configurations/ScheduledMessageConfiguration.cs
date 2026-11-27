using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class ScheduledMessageConfiguration : IEntityTypeConfiguration<ScheduledMessage>
{
    public void Configure(EntityTypeBuilder<ScheduledMessage> builder)
    {
        builder.ToTable("Scheduled_Messages");
        builder.HasKey(e => e.ScheduledMessageId).HasName("Scheduled_Messages_pkey");

        builder.Property(e => e.ScheduledMessageId)
            .HasColumnName("Scheduled_Message_ID")
            .ValueGeneratedNever();

        builder.Property(e => e.ChatId).HasColumnName("Chat_ID");
        builder.Property(e => e.SenderId).HasColumnName("Sender_ID");
        builder.Property(e => e.MessageText).HasColumnName("Message_Text");
        builder.Property(e => e.HasAttachments).HasColumnName("Has_Attachments");
        builder.Property(e => e.AttachmentsJson).HasColumnName("Attachments_Json");
        builder.Property(e => e.ScheduledAt)
            .HasColumnName("Scheduled_At")
            .HasColumnType("timestamp with time zone");
        builder.Property(e => e.CreatedAt)
            .HasColumnName("Created_At")
            .HasColumnType("timestamp with time zone");
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("Updated_At")
            .HasColumnType("timestamp with time zone");
        builder.Property(e => e.Status)
            .HasColumnName("Status")
            .HasMaxLength(32);
        builder.Property(e => e.ResultMessageId).HasColumnName("Result_Message_ID");
        builder.Property(e => e.ErrorMessage).HasColumnName("Error_Message");
        builder.Property(e => e.ReplyToMessageId).HasColumnName("Reply_To_Message_ID");

        builder.HasIndex(e => e.ChatId).HasDatabaseName("IX_Scheduled_Messages_Chat_ID");
        builder.HasIndex(e => new { e.SenderId, e.Status })
            .HasDatabaseName("IX_Scheduled_Messages_Sender_ID_Status");
        builder.HasIndex(e => new { e.Status, e.ScheduledAt })
            .HasDatabaseName("IX_Scheduled_Messages_Status_Scheduled_At");

        builder.HasOne(d => d.Chat)
            .WithMany()
            .HasForeignKey(d => d.ChatId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_scheduled_message_chat");

        builder.HasOne(d => d.Sender)
            .WithMany()
            .HasForeignKey(d => d.SenderId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_scheduled_message_sender");
    }
}
