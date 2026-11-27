using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Data.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.HasKey(e => e.RoleId).HasName("Roles_pkey");
        builder.Property(e => e.RoleId).ValueGeneratedNever();

        builder.HasData(
            new Role
            {
                RoleId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
                Name = "Абитуриент",
                Description = "Роль абитуриента (ROLE_ENTRANT)"
            },
            new Role
            {
                RoleId = Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901"),
                Name = "Студент",
                Description = "Роль студента (ROLE_STUDENT)"
            },
            new Role
            {
                RoleId = Guid.Parse("c3d4e5f6-a7b8-9012-cdef-123456789012"),
                Name = "Преподаватель",
                Description = "Роль преподавателя (ROLE_TEACHER)"
            },
            new Role
            {
                RoleId = Guid.Parse("d4e5f6a7-b8c9-0123-defa-234567890123"),
                Name = "Сотрудник",
                Description = "Роль сотрудника (ROLE_EMPLOYEE)"
            },
            new Role
            {
                RoleId = Guid.Parse("e5f6a7b8-c9d0-1234-efab-345678901234"),
                Name = "Администратор",
                Description = "Полный доступ к системе (ROLE_ADMIN)"
            },
            new Role
            {
                RoleId = Guid.Parse("f6a7b8c9-d0e1-2345-fabc-456789012345"),
                Name = "Пользователь",
                Description = "Стандартный доступ пользователя (ROLE_USER)"
            });
    }
}
