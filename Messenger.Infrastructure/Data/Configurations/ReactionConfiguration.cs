using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class ReactionConfiguration : IEntityTypeConfiguration<Reaction>
{
    public void Configure(EntityTypeBuilder<Reaction> builder)
    {
        builder.HasKey(e => e.ReactionId).HasName("Reactions_pkey");
        builder.Property(e => e.ReactionId).ValueGeneratedNever();
        builder.HasIndex(e => new { e.MessageId, e.UserId })
            .IsUnique().HasDatabaseName("IX_Reactions_MessageId_UserId");
        builder.HasOne(d => d.Message).WithMany(p => p.Reactions)
            .HasConstraintName("fk_reaction_message");
        builder.HasOne(d => d.User).WithMany(p => p.Reactions)
            .HasConstraintName("fk_reaction_user");
    }
}
