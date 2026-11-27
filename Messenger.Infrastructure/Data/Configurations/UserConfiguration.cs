using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(e => e.UserId).HasName("Users_pkey");
        builder.Property(e => e.UserId).ValueGeneratedNever();
        builder.HasMany(d => d.Roles).WithMany(p => p.Users)
            .UsingEntity<Dictionary<string, object>>(
                "UserRole",
                r => r.HasOne<Role>().WithMany()
                    .HasForeignKey("RoleId")
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("fk_userrole_role"),
                l => l.HasOne<User>().WithMany()
                    .HasForeignKey("UserId")
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("fk_userrole_user"),
                j =>
                {
                    j.HasKey("UserId", "RoleId").HasName("UserRoles_pkey");
                    j.ToTable("User_Roles");
                    j.IndexerProperty<Guid>("UserId").HasColumnName("User_ID");
                    j.IndexerProperty<Guid>("RoleId").HasColumnName("Role_ID");
                });
    }
}
