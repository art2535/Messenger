using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Messenger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNewAccountSettingsColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Compact_List",
                table: "Account_Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Idle_Logout",
                table: "Account_Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Large_Font",
                table: "Account_Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Notify_Preview",
                table: "Account_Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Notify_Reactions",
                table: "Account_Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Notify_Sound",
                table: "Account_Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Notify_System",
                table: "Account_Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Read_Receipts",
                table: "Account_Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Show_Online",
                table: "Account_Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Show_Typing",
                table: "Account_Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Suppress_Open_Chat",
                table: "Account_Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Theme",
                table: "Account_Settings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Compact_List",
                table: "Account_Settings");

            migrationBuilder.DropColumn(
                name: "Idle_Logout",
                table: "Account_Settings");

            migrationBuilder.DropColumn(
                name: "Large_Font",
                table: "Account_Settings");

            migrationBuilder.DropColumn(
                name: "Notify_Preview",
                table: "Account_Settings");

            migrationBuilder.DropColumn(
                name: "Notify_Reactions",
                table: "Account_Settings");

            migrationBuilder.DropColumn(
                name: "Notify_Sound",
                table: "Account_Settings");

            migrationBuilder.DropColumn(
                name: "Notify_System",
                table: "Account_Settings");

            migrationBuilder.DropColumn(
                name: "Read_Receipts",
                table: "Account_Settings");

            migrationBuilder.DropColumn(
                name: "Show_Online",
                table: "Account_Settings");

            migrationBuilder.DropColumn(
                name: "Show_Typing",
                table: "Account_Settings");

            migrationBuilder.DropColumn(
                name: "Suppress_Open_Chat",
                table: "Account_Settings");

            migrationBuilder.DropColumn(
                name: "Theme",
                table: "Account_Settings");
        }
    }
}
