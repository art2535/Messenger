using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Messenger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoggingSchemaAndLogsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "logging");

            migrationBuilder.CreateTable(
                name: "Logs",
                schema: "logging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Message = table.Column<string>(type: "text", nullable: true),
                    MessageTemplate = table.Column<string>(type: "text", nullable: true),
                    Level = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Exception = table.Column<string>(type: "text", nullable: true),
                    LogEvent = table.Column<string>(type: "jsonb", nullable: true),
                    Application = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    MachineName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Environment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("logs_pkey", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_logs_application",
                schema: "logging",
                table: "Logs",
                column: "Application");

            migrationBuilder.CreateIndex(
                name: "IX_logs_level",
                schema: "logging",
                table: "Logs",
                column: "Level");

            migrationBuilder.CreateIndex(
                name: "IX_logs_timestamp",
                schema: "logging",
                table: "Logs",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Logs",
                schema: "logging");
        }
    }
}
