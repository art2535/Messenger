using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.HasKey(e => e.MessageId).HasName("Messages_pkey");
        builder.Property(e => e.MessageId).ValueGeneratedNever();
        builder.Property(e => e.SendTime).HasColumnType("timestamp with time zone");
        builder.Property(e => e.ReadTime).HasColumnType("timestamp with time zone");
        builder.HasIndex(e => new { e.ChatId, e.SequenceNumber })
            .IsUnique().HasDatabaseName("IX_Messages_ChatId_SequenceNumber");
        builder.HasIndex(e => new { e.ChatId, e.SendTime })
            .HasDatabaseName("IX_Messages_ChatId_SendTime");
        builder.HasIndex(e => new { e.ChatId, e.ReadTime })
            .HasDatabaseName("IX_Messages_ChatId_ReadTime");
        builder.Property(e => e.IsPinned).HasColumnName("Is_Pinned").HasDefaultValue(false);
        builder.Property(e => e.PinnedAt)
            .HasColumnName("Pinned_At").HasColumnType("timestamp with time zone");
        builder.HasIndex(e => new { e.ChatId, e.IsPinned })
            .HasDatabaseName("IX_Messages_ChatId_IsPinned")
            .HasFilter("\"Is_Pinned\" = true");
        builder.HasOne(d => d.Chat).WithMany(p => p.Messages)
            .HasConstraintName("fk_chat_messages");
        builder.HasOne(d => d.Sender).WithMany(p => p.MessageSenders)
            .HasConstraintName("fk_sender");
    }
}
