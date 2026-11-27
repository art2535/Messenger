using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments");

        builder.HasKey(e => e.AttachmentId).HasName("Attachments_pkey");
        builder.Property(e => e.AttachmentId)
            .HasColumnName("Attachment_ID")
            .ValueGeneratedNever();

        builder.Property(e => e.MessageId).HasColumnName("Message_ID");
        builder.Property(e => e.FileName).HasColumnName("File_Name").HasMaxLength(255).IsRequired();
        builder.Property(e => e.FileType).HasColumnName("File_Type").HasMaxLength(100);
        builder.Property(e => e.SizeInBytes).HasColumnName("Size_in_Bytes");
        builder.Property(e => e.Url).HasColumnName("URL").IsRequired();

        builder.HasIndex(e => e.MessageId)
            .HasDatabaseName("IX_Attachments_MessageId");

        builder.HasOne(d => d.Message)
            .WithMany(p => p.Attachments)
            .HasForeignKey(d => d.MessageId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired()
            .HasConstraintName("fk_attachment_message");
    }
}
