using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Messenger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedInitialRolesData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Role_ID", "Description", "Name" },
                values: new object[,]
                {
                    { new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890"), "Роль абитуриента (ROLE_ENTRANT)", "Абитуриент" },
                    { new Guid("b2c3d4e5-f6a7-8901-bcde-f12345678901"), "Роль студента (ROLE_STUDENT)", "Студент" },
                    { new Guid("c3d4e5f6-a7b8-9012-cdef-123456789012"), "Роль преподавателя (ROLE_TEACHER)", "Преподаватель" },
                    { new Guid("d4e5f6a7-b8c9-0123-defa-234567890123"), "Роль сотрудника (ROLE_EMPLOYEE)", "Сотрудник" },
                    { new Guid("e5f6a7b8-c9d0-1234-efab-345678901234"), "Полный доступ к системе (ROLE_ADMIN)", "Администратор" },
                    { new Guid("f6a7b8c9-d0e1-2345-fabc-456789012345"), "Стандартный доступ пользователя (ROLE_USER)", "Пользователь" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Role_ID",
                keyValue: new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890"));

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Role_ID",
                keyValue: new Guid("b2c3d4e5-f6a7-8901-bcde-f12345678901"));

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Role_ID",
                keyValue: new Guid("c3d4e5f6-a7b8-9012-cdef-123456789012"));

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Role_ID",
                keyValue: new Guid("d4e5f6a7-b8c9-0123-defa-234567890123"));

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Role_ID",
                keyValue: new Guid("e5f6a7b8-c9d0-1234-efab-345678901234"));

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Role_ID",
                keyValue: new Guid("f6a7b8c9-d0e1-2345-fabc-456789012345"));
        }
    }
}
