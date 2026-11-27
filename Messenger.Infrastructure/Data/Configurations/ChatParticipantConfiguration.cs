using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class ChatParticipantConfiguration : IEntityTypeConfiguration<ChatParticipant>
{
    public void Configure(EntityTypeBuilder<ChatParticipant> builder)
    {
        builder.ToTable("Chat_Participants");

        builder.HasKey(e => new { e.ChatId, e.UserId }).HasName("Chat_Participants_pkey");
        builder.Property(e => e.ChatId).HasColumnName("Chat_ID");
        builder.Property(e => e.UserId).HasColumnName("User_ID");
        builder.Property(e => e.JoinDate)
            .HasColumnName("Join_Date")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now() at time zone 'utc'");
        builder.Property(e => e.Role)
            .HasMaxLength(20)
            .HasDefaultValueSql("'participant'::character varying");

        builder.HasIndex(e => e.UserId).HasDatabaseName("IX_ChatParticipants_UserId");

        builder.HasOne(d => d.Chat)
            .WithMany(p => p.ChatParticipants)
            .HasForeignKey(d => d.ChatId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired()
            .HasConstraintName("fk_participant_chat");

        builder.HasOne(d => d.User)
            .WithMany(p => p.ChatParticipants)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired()
            .HasConstraintName("fk_participant_user");
    }
}
