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
        builder.Property(e => e.ReportId).HasColumnName("Report_ID").ValueGeneratedOnAdd();
        builder.Property(e => e.MessageId).HasColumnName("Message_ID");
        builder.Property(e => e.ChatId).HasColumnName("Chat_ID");
        builder.Property(e => e.ReporterId).HasColumnName("Reporter_ID");
        builder.Property(e => e.Reason).HasColumnName("Reason").HasMaxLength(64).IsRequired();
        builder.Property(e => e.Comment).HasColumnName("Comment").HasMaxLength(1000);
        builder.Property(e => e.Status).HasColumnName("Status").HasMaxLength(32).IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("Created_At").HasColumnType("timestamp with time zone");
        builder.Property(e => e.ReviewedAt).HasColumnName("Reviewed_At").HasColumnType("timestamp with time zone");
        builder.Property(e => e.ReviewedBy).HasColumnName("Reviewed_By");
        builder.Property(e => e.AdminNote).HasColumnName("Admin_Note").HasMaxLength(1000);

        builder.HasIndex(e => e.MessageId);
        builder.HasIndex(e => e.Status);

        builder.HasOne(d => d.Message)
            .WithMany()
            .HasForeignKey(d => d.MessageId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
