using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Messenger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Scheduled_Messages",
                columns: table => new
                {
                    Scheduled_Message_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Chat_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Sender_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Message_Text = table.Column<string>(type: "text", nullable: true),
                    Has_Attachments = table.Column<bool>(type: "boolean", nullable: false),
                    Attachments_Json = table.Column<string>(type: "text", nullable: true),
                    Scheduled_At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Created_At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Updated_At = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Result_Message_ID = table.Column<Guid>(type: "uuid", nullable: true),
                    Error_Message = table.Column<string>(type: "text", nullable: true),
                    Reply_To_Message_ID = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Scheduled_Messages_pkey", x => x.Scheduled_Message_ID);
                    table.ForeignKey(
                        name: "fk_scheduled_message_chat",
                        column: x => x.Chat_ID,
                        principalTable: "Chats",
                        principalColumn: "Chat_ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scheduled_message_sender",
                        column: x => x.Sender_ID,
                        principalTable: "Users",
                        principalColumn: "User_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Scheduled_Messages_Chat_ID",
                table: "Scheduled_Messages",
                column: "Chat_ID");

            migrationBuilder.CreateIndex(
                name: "IX_Scheduled_Messages_Sender_ID_Status",
                table: "Scheduled_Messages",
                columns: new[] { "Sender_ID", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Scheduled_Messages_Status_Scheduled_At",
                table: "Scheduled_Messages",
                columns: new[] { "Status", "Scheduled_At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Scheduled_Messages");
        }
    }
}
