using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class ChatParticipantConfiguration : IEntityTypeConfiguration<ChatParticipant>
{
    public void Configure(EntityTypeBuilder<ChatParticipant> builder)
    {
        builder.HasKey(e => new { e.ChatId, e.UserId }).HasName("Chat_Participants_pkey");
        builder.Property(e => e.JoinDate)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now() at time zone 'utc'");
        builder.Property(e => e.Role).HasDefaultValueSql("'participant'::character varying");
        builder.HasIndex(e => e.UserId).HasDatabaseName("IX_ChatParticipants_UserId");
        builder.HasOne(d => d.Chat).WithMany(p => p.ChatParticipants)
            .HasConstraintName("fk_participant_chat");
        builder.HasOne(d => d.User).WithMany(p => p.ChatParticipants)
            .HasConstraintName("fk_participant_user");
    }
}
