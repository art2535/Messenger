using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Messenger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixAccountSettingsDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Account_Settings" SET
                    "Idle_Logout" = true,
                    "Notify_Preview" = true,
                    "Notify_Reactions" = true,
                    "Notify_Sound" = true,
                    "Notify_System" = true,
                    "Read_Receipts" = true,
                    "Show_Online" = true,
                    "Show_Typing" = true,
                    "Suppress_Open_Chat" = true,
                    "Theme" = CASE
                        WHEN "Theme" IS NULL OR btrim("Theme") = '' THEN 'system'
                        ELSE "Theme"
                    END
                WHERE
                    "Show_Online" = false
                    OR "Show_Typing" = false
                    OR "Read_Receipts" = false
                    OR "Notify_Sound" = false
                    OR "Notify_Preview" = false
                    OR "Suppress_Open_Chat" = false
                    OR "Idle_Logout" = false
                    OR "Theme" IS NULL
                    OR btrim("Theme") = '';
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "Account_Settings"
                    ALTER COLUMN "Idle_Logout" SET DEFAULT true,
                    ALTER COLUMN "Notify_Preview" SET DEFAULT true,
                    ALTER COLUMN "Notify_Reactions" SET DEFAULT true,
                    ALTER COLUMN "Notify_Sound" SET DEFAULT true,
                    ALTER COLUMN "Notify_System" SET DEFAULT true,
                    ALTER COLUMN "Read_Receipts" SET DEFAULT true,
                    ALTER COLUMN "Show_Online" SET DEFAULT true,
                    ALTER COLUMN "Show_Typing" SET DEFAULT true,
                    ALTER COLUMN "Suppress_Open_Chat" SET DEFAULT true,
                    ALTER COLUMN "Theme" SET DEFAULT 'system';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Account_Settings"
                    ALTER COLUMN "Idle_Logout" SET DEFAULT false,
                    ALTER COLUMN "Notify_Preview" SET DEFAULT false,
                    ALTER COLUMN "Notify_Reactions" SET DEFAULT false,
                    ALTER COLUMN "Notify_Sound" SET DEFAULT false,
                    ALTER COLUMN "Notify_System" SET DEFAULT false,
                    ALTER COLUMN "Read_Receipts" SET DEFAULT false,
                    ALTER COLUMN "Show_Online" SET DEFAULT false,
                    ALTER COLUMN "Show_Typing" SET DEFAULT false,
                    ALTER COLUMN "Suppress_Open_Chat" SET DEFAULT false,
                    ALTER COLUMN "Theme" SET DEFAULT '';
                """);
        }

    }
}
