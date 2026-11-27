using Messenger.Core.Models;
using Messenger.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.HasKey(e => e.Id).HasName("PushSubscriptions_pkey");
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.HasIndex(e => e.UserId)
            .HasDatabaseName("IX_PushSubscriptions_UserId");

        builder.HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_push_subscription_user");
    }
}
