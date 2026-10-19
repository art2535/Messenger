using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Messenger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeAccountSettingsOneToOneWithSharedKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_account",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Account_ID",
                table: "Users");

            migrationBuilder.DropPrimaryKey(
                name: "Account_Settings_pkey",
                table: "Account_Settings");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Account_Settings_Account_ID",
                table: "Account_Settings");

            migrationBuilder.DropColumn(
                name: "Setting_ID",
                table: "Account_Settings");

            migrationBuilder.AddPrimaryKey(
                name: "Account_Settings_pkey",
                table: "Account_Settings",
                column: "Account_ID");

            migrationBuilder.AddForeignKey(
                name: "fk_account",
                table: "Account_Settings",
                column: "Account_ID",
                principalTable: "Users",
                principalColumn: "User_ID",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_account",
                table: "Account_Settings");

            migrationBuilder.DropPrimaryKey(
                name: "Account_Settings_pkey",
                table: "Account_Settings");

            migrationBuilder.AddColumn<Guid>(
                name: "Setting_ID",
                table: "Account_Settings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "Account_Settings_pkey",
                table: "Account_Settings",
                column: "Setting_ID");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Account_Settings_Account_ID",
                table: "Account_Settings",
                column: "Account_ID");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Account_ID",
                table: "Users",
                column: "Account_ID");

            migrationBuilder.AddForeignKey(
                name: "fk_account",
                table: "Users",
                column: "Account_ID",
                principalTable: "Account_Settings",
                principalColumn: "Account_ID",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
