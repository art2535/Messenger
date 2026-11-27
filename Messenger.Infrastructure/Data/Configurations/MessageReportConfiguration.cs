using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class MessageReportConfiguration : IEntityTypeConfiguration<MessageReport>
{
    public void Configure(EntityTypeBuilder<MessageReport> builder)
    {
        builder.ToTable("Message_Reports");
        builder.HasKey(e => e.ReportId);
        builder.Property(e => e.ReportId).HasColumnName("Report_ID");
        builder.Property(e => e.MessageId).HasColumnName("Message_ID");
        builder.Property(e => e.ChatId).HasColumnName("Chat_ID");
        builder.Property(e => e.ReporterId).HasColumnName("Reporter_ID");
        builder.Property(e => e.CreatedAt).HasColumnName("Created_At");
        builder.Property(e => e.ReviewedAt).HasColumnName("Reviewed_At");
        builder.Property(e => e.ReviewedBy).HasColumnName("Reviewed_By");
        builder.Property(e => e.AdminNote).HasColumnName("Admin_Note");
        builder.HasIndex(e => e.Status);
    }
}
