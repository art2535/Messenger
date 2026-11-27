using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.HasKey(e => e.AttachmentId).HasName("Attachments_pkey");
        builder.Property(e => e.AttachmentId).ValueGeneratedNever();

        builder.HasIndex(e => e.MessageId)
            .HasDatabaseName("IX_Attachments_MessageId");

        builder.HasOne(d => d.Message)
            .WithMany(p => p.Attachments)
            .HasConstraintName("fk_attachment_message");
    }
}
