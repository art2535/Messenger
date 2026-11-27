using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class ChatFolderConfiguration : IEntityTypeConfiguration<ChatFolder>
{
    public void Configure(EntityTypeBuilder<ChatFolder> builder)
    {
        builder.ToTable("ChatFolders");
        builder.HasKey(e => e.FolderId);
        builder.Property(e => e.Name).HasMaxLength(64).IsRequired();
        builder.HasIndex(e => e.UserId);
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
