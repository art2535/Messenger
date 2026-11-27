using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(e => e.UserId).HasName("Users_pkey");
        builder.Property(e => e.UserId)
            .HasColumnName("User_ID")
            .ValueGeneratedNever();

        builder.Property(e => e.LastName)
            .HasColumnName("Last_Name")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.FirstName)
            .HasColumnName("First_Name")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.RegistrationDate)
            .HasColumnName("Registration_Date");

        builder.Property(e => e.Login)
            .HasMaxLength(50);

        builder.Property(e => e.ExternalId)
            .HasColumnName("External_ID")
            .HasMaxLength(255);

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
