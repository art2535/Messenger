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
        builder.HasIndex(e => new { e.FolderId, e.ChatId }).IsUnique();
        builder.HasOne(e => e.Folder)
            .WithMany(f => f.Chats)
            .HasForeignKey(e => e.FolderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Chat)
            .WithMany()
            .HasForeignKey(e => e.ChatId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
