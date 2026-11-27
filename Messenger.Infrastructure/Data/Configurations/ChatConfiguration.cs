using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class ChatConfiguration : IEntityTypeConfiguration<Chat>
{
    public void Configure(EntityTypeBuilder<Chat> builder)
    {
        builder.HasKey(e => e.ChatId).HasName("Chats_pkey");
        builder.Property(e => e.ChatId).ValueGeneratedNever();
        builder.Property(e => e.CreationDate)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now() at time zone 'utc'");
        builder.HasOne(d => d.User)
            .WithMany(p => p.Chats)
            .HasConstraintName("fk_chat_creator");
    }
}
