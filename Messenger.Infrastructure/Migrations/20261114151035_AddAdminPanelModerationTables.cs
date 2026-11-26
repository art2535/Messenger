using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Messenger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminPanelModerationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Message_Reports",
                columns: table => new
                {
                    Report_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Message_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Chat_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Reporter_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Created_At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reviewed_At = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reviewed_By = table.Column<Guid>(type: "uuid", nullable: true),
                    Admin_Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Message_Reports", x => x.Report_ID);
                    table.ForeignKey(
                        name: "FK_Message_Reports_Messages_Message_ID",
                        column: x => x.Message_ID,
                        principalTable: "Messages",
                        principalColumn: "Message_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Moderation_Logs",
                columns: table => new
                {
                    Log_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Actor_ID = table.Column<Guid>(type: "uuid", nullable: true),
                    Target_User_ID = table.Column<Guid>(type: "uuid", nullable: true),
                    Chat_ID = table.Column<Guid>(type: "uuid", nullable: true),
                    Message_ID = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Created_At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Moderation_Logs", x => x.Log_ID);
                });

            migrationBuilder.CreateTable(
                name: "User_Mutes",
                columns: table => new
                {
                    Mute_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    User_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Chat_ID = table.Column<Guid>(type: "uuid", nullable: true),
                    Muted_By = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Muted_Until = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Created_At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Is_Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User_Mutes", x => x.Mute_ID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Message_Reports_Message_ID",
                table: "Message_Reports",
                column: "Message_ID");

            migrationBuilder.CreateIndex(
                name: "IX_Message_Reports_Status",
                table: "Message_Reports",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Message_Reports");

            migrationBuilder.DropTable(
                name: "Moderation_Logs");

            migrationBuilder.DropTable(
                name: "User_Mutes");
        }
    }
}
