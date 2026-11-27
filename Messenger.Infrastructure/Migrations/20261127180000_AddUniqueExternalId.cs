using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Messenger.Infrastructure.Data;

#nullable disable

namespace Messenger.Infrastructure.Migrations;

[DbContext(typeof(GuapMessengerContext))]
[Migration("20261127180000_AddUniqueExternalId")]
public partial class AddUniqueExternalId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // До нормализации проверяем дубликаты с учётом пробелов по краям.
        // Миграция намеренно завершается ошибкой, а не выбирает произвольную запись.
        migrationBuilder.Sql("""
            DO $$
            DECLARE duplicate_values text;
            BEGIN
                SELECT string_agg(format('%s (%s записей)', external_id, duplicate_count), '; ')
                INTO duplicate_values
                FROM (
                    SELECT btrim("External_ID") AS external_id, COUNT(*) AS duplicate_count
                    FROM "Users"
                    WHERE "External_ID" IS NOT NULL
                      AND btrim("External_ID") <> ''
                    GROUP BY btrim("External_ID")
                    HAVING COUNT(*) > 1
                ) duplicates;

                IF duplicate_values IS NOT NULL THEN
                    RAISE EXCEPTION 'Нельзя создать уникальный индекс External_ID: найдены дубликаты: %', duplicate_values;
                END IF;
            END $$;
            """);

        // Пустые и состоящие из пробелов значения считаются отсутствующим External_ID.
        migrationBuilder.Sql("""
            UPDATE "Users"
            SET "External_ID" = NULLIF(btrim("External_ID"), '')
            WHERE "External_ID" IS NOT NULL
              AND "External_ID" IS DISTINCT FROM NULLIF(btrim("External_ID"), '');
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Users_External_ID",
            table: "Users",
            column: "External_ID",
            unique: true,
            filter: "\"External_ID\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Users_External_ID",
            table: "Users");
    }
}
