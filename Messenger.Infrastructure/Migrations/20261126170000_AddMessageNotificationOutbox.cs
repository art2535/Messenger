using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Messenger.Infrastructure.Data;

#nullable disable

namespace Messenger.Infrastructure.Migrations;

[DbContext(typeof(GuapMessengerContext))]
[Migration("20261126170000_AddMessageNotificationOutbox")]
public partial class AddMessageNotificationOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Message_Notification_Outbox",
            columns: table => new
            {
                Outbox_ID = table.Column<Guid>(type: "uuid", nullable: false),
                Message_ID = table.Column<Guid>(type: "uuid", nullable: false),
                Chat_ID = table.Column<Guid>(type: "uuid", nullable: false),
                Sender_ID = table.Column<Guid>(type: "uuid", nullable: false),
                Sender_Name = table.Column<string>(type: "text", nullable: false),
                Payload = table.Column<string>(type: "text", nullable: false),
                Message_Text = table.Column<string>(type: "text", nullable: true),
                Has_Attachments = table.Column<bool>(type: "boolean", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Attempt_Count = table.Column<int>(type: "integer", nullable: false),
                Next_Attempt_At = table.Column<DateTime>(
                    type: "timestamp with time zone", nullable: false),
                Locked_Until = table.Column<DateTime>(
                    type: "timestamp with time zone", nullable: true),
                Created_At = table.Column<DateTime>(
                    type: "timestamp with time zone", nullable: false),
                Delivered_At = table.Column<DateTime>(
                    type: "timestamp with time zone", nullable: true),
                Last_Error = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("Message_Notification_Outbox_pkey", x => x.Outbox_ID);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Message_Notification_Outbox_Message_ID",
            table: "Message_Notification_Outbox",
            column: "Message_ID",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Message_Notification_Outbox_Status_Next_Attempt_At",
            table: "Message_Notification_Outbox",
            columns: new[] { "Status", "Next_Attempt_At" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Message_Notification_Outbox");
    }
}
