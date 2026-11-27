using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Messenger.Infrastructure.Data;

#nullable disable

namespace Messenger.Infrastructure.Migrations;

[DbContext(typeof(GuapMessengerContext))]
[Migration("20261127193000_AddNotificationDeduplicationKey")]
public partial class AddNotificationDeduplicationKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Deduplication_Key",
            table: "Notifications",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Notifications_Deduplication_Key",
            table: "Notifications",
            column: "Deduplication_Key",
            unique: true,
            filter: "\"Deduplication_Key\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Notifications_Deduplication_Key",
            table: "Notifications");

        migrationBuilder.DropColumn(
            name: "Deduplication_Key",
            table: "Notifications");
    }
}