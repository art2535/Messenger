using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Messenger.Infrastructure.Data;

#nullable disable

namespace Messenger.Infrastructure.Migrations;

[DbContext(typeof(GuapMessengerContext))]
[Migration("20261126182000_AddMessageNotificationOutboxDeliveries")]
public partial class AddMessageNotificationOutboxDeliveries : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Message_Notification_Outbox_Deliveries",
            columns: table => new
            {
                Delivery_ID = table.Column<Guid>(type: "uuid", nullable: false),
                Outbox_ID = table.Column<Guid>(type: "uuid", nullable: false),
                Message_ID = table.Column<Guid>(type: "uuid", nullable: false),
                Recipient_ID = table.Column<Guid>(type: "uuid", nullable: false),
                Subscription_ID = table.Column<Guid>(type: "uuid", nullable: false),
                Channel = table.Column<string>(
                    type: "character varying(32)",
                    maxLength: 32,
                    nullable: false),
                Status = table.Column<string>(
                    type: "character varying(32)",
                    maxLength: 32,
                    nullable: false),
                Attempt_Count = table.Column<int>(type: "integer", nullable: false),
                Next_Attempt_At = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false),
                Locked_Until = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true),
                Notification_ID = table.Column<Guid>(type: "uuid", nullable: true),
                Idempotency_Key = table.Column<string>(
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: false),
                Delivered_At = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true),
                Last_Error = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "Message_Notification_Outbox_Deliveries_pkey",
                    x => x.Delivery_ID);
                table.ForeignKey(
                    name: "FK_Message_Notification_Outbox_Deliveries_Outbox",
                    column: x => x.Outbox_ID,
                    principalTable: "Message_Notification_Outbox",
                    principalColumn: "Outbox_ID",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Message_Notification_Outbox_Deliveries_Outbox_ID",
            table: "Message_Notification_Outbox_Deliveries",
            column: "Outbox_ID");

        migrationBuilder.CreateIndex(
            name: "IX_Message_Notification_Outbox_Deliveries_Idempotency_Key",
            table: "Message_Notification_Outbox_Deliveries",
            column: "Idempotency_Key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Message_Notification_Outbox_Deliveries_Outbox_Status_Next",
            table: "Message_Notification_Outbox_Deliveries",
            columns: new[] { "Outbox_ID", "Status", "Next_Attempt_At" });

        migrationBuilder.CreateIndex(
            name: "IX_Message_Notification_Outbox_Deliveries_Recipient_Subscription",
            table: "Message_Notification_Outbox_Deliveries",
            columns: new[] { "Recipient_ID", "Subscription_ID" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Message_Notification_Outbox_Deliveries");
    }
}
