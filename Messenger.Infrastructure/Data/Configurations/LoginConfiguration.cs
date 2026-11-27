using Messenger.Core.Models;
using Messenger.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class LoginConfiguration : IEntityTypeConfiguration<Login>
{
    public void Configure(EntityTypeBuilder<Login> builder)
    {
        builder.HasKey(e => e.LoginId).HasName("Logins_pkey");
        builder.Property(e => e.LoginId).ValueGeneratedNever();
        builder.Property(e => e.LoginTime).HasColumnType("timestamp with time zone");
        builder.Property(e => e.LogoutTime).HasColumnType("timestamp with time zone");

        builder.HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_login_user");
    }
}
