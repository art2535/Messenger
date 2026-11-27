using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class ChatConfiguration : IEntityTypeConfiguration<Chat>
{
    public void Configure(EntityTypeBuilder<Chat> builder)
    {
        builder.ToTable("Chats");

        builder.HasKey(e => e.ChatId).HasName("Chats_pkey");
        builder.Property(e => e.ChatId)
            .HasColumnName("Chat_ID")
            .ValueGeneratedNever();

        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Type).HasMaxLength(20).IsRequired();
        builder.Property(e => e.UserId).HasColumnName("User_ID");
        builder.Property(e => e.CreationDate)
            .HasColumnName("Creation_Date")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now() at time zone 'utc'");

        builder.HasOne(d => d.User)
            .WithMany(p => p.Chats)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired()
            .HasConstraintName("fk_chat_creator");
    }
}
