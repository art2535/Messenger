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
        builder.Property(e => e.FolderId)
            .HasColumnName("Folder_ID")
            .ValueGeneratedOnAdd();
        builder.Property(e => e.UserId).HasColumnName("User_ID");
        builder.Property(e => e.Name).HasMaxLength(64).IsRequired();
        builder.Property(e => e.SortOrder).HasColumnName("Sort_Order");
        builder.Property(e => e.CreatedAt)
            .HasColumnName("Created_At")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(e => e.UserId);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
