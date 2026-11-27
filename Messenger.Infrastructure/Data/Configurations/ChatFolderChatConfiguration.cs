using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class ChatFolderChatConfiguration : IEntityTypeConfiguration<ChatFolderChat>
{
    public void Configure(EntityTypeBuilder<ChatFolderChat> builder)
    {
        builder.ToTable("ChatFolderChats");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();
        builder.Property(e => e.FolderId).HasColumnName("Folder_ID");
        builder.Property(e => e.ChatId).HasColumnName("Chat_ID");

        builder.HasIndex(e => e.ChatId);
        builder.HasIndex(e => new { e.FolderId, e.ChatId }).IsUnique();

        builder.HasOne(e => e.Folder)
            .WithMany(f => f.Chats)
            .HasForeignKey(e => e.FolderId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne(e => e.Chat)
            .WithMany()
            .HasForeignKey(e => e.ChatId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
