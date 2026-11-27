using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class UserMuteConfiguration : IEntityTypeConfiguration<UserMute>
{
    public void Configure(EntityTypeBuilder<UserMute> builder)
    {
        builder.ToTable("User_Mutes");
        builder.HasKey(e => e.MuteId);
        builder.Property(e => e.MuteId).HasColumnName("Mute_ID");
        builder.Property(e => e.UserId).HasColumnName("User_ID");
        builder.Property(e => e.ChatId).HasColumnName("Chat_ID");
        builder.Property(e => e.MutedBy).HasColumnName("Muted_By");
        builder.Property(e => e.MutedUntil).HasColumnName("Muted_Until");
        builder.Property(e => e.CreatedAt).HasColumnName("Created_At");
        builder.Property(e => e.IsActive).HasColumnName("Is_Active");
    }
}
