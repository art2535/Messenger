using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Messenger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeBroadcastSenderOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "Sender_ID",
                table: "Broadcasts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Откат возможен только если все рассылки снова имеют Sender_ID.
            migrationBuilder.AlterColumn<Guid>(
                name: "Sender_ID",
                table: "Broadcasts",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid?),
                oldType: "uuid");
        }
    }
}
