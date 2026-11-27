using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class ReactionConfiguration : IEntityTypeConfiguration<Reaction>
{
    public void Configure(EntityTypeBuilder<Reaction> builder)
    {
        builder.ToTable("Reactions");

        builder.HasKey(e => e.ReactionId).HasName("Reactions_pkey");
        builder.Property(e => e.ReactionId).HasColumnName("Reaction_ID").ValueGeneratedNever();
        builder.Property(e => e.MessageId).HasColumnName("Message_ID");
        builder.Property(e => e.UserId).HasColumnName("User_ID");
        builder.Property(e => e.ReactionType).HasColumnName("Reaction_Type").HasMaxLength(30).IsRequired();

        builder.HasIndex(e => new { e.MessageId, e.UserId })
            .IsUnique().HasDatabaseName("IX_Reactions_MessageId_UserId");

        builder.HasOne(d => d.Message)
            .WithMany(p => p.Reactions)
            .HasForeignKey(d => d.MessageId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired()
            .HasConstraintName("fk_reaction_message");

        builder.HasOne(d => d.User)
            .WithMany(p => p.Reactions)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired()
            .HasConstraintName("fk_reaction_user");
    }
}
