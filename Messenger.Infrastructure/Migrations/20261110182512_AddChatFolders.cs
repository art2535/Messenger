using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Messenger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatFolders",
                columns: table => new
                {
                    Folder_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    User_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Sort_Order = table.Column<int>(type: "integer", nullable: false),
                    Created_At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatFolders", x => x.Folder_ID);
                    table.ForeignKey(
                        name: "FK_ChatFolders_Users_User_ID",
                        column: x => x.User_ID,
                        principalTable: "Users",
                        principalColumn: "User_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatFolderChats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Folder_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Chat_ID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatFolderChats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatFolderChats_ChatFolders_Folder_ID",
                        column: x => x.Folder_ID,
                        principalTable: "ChatFolders",
                        principalColumn: "Folder_ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatFolderChats_Chats_Chat_ID",
                        column: x => x.Chat_ID,
                        principalTable: "Chats",
                        principalColumn: "Chat_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatFolderChats_Chat_ID",
                table: "ChatFolderChats",
                column: "Chat_ID");

            migrationBuilder.CreateIndex(
                name: "IX_ChatFolderChats_Folder_ID_Chat_ID",
                table: "ChatFolderChats",
                columns: new[] { "Folder_ID", "Chat_ID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatFolders_User_ID",
                table: "ChatFolders",
                column: "User_ID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatFolderChats");

            migrationBuilder.DropTable(
                name: "ChatFolders");
        }
    }
}
