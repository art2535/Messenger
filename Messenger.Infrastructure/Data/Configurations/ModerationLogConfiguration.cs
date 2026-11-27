using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class ModerationLogConfiguration : IEntityTypeConfiguration<ModerationLog>
{
    public void Configure(EntityTypeBuilder<ModerationLog> builder)
    {
        builder.ToTable("Moderation_Logs");
        builder.HasKey(e => e.LogId);
        builder.Property(e => e.LogId).HasColumnName("Log_ID");
        builder.Property(e => e.ActorId).HasColumnName("Actor_ID");
        builder.Property(e => e.TargetUserId).HasColumnName("Target_User_ID");
        builder.Property(e => e.ChatId).HasColumnName("Chat_ID");
        builder.Property(e => e.MessageId).HasColumnName("Message_ID");
        builder.Property(e => e.CreatedAt).HasColumnName("Created_At");
    }
}
