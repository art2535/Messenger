using Messenger.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Extensions
{
    public static class DatabaseExtension
    {
        extension(WebApplication app)
        {
            public WebApplication InitializeMessengerDatabase()
            {
                using var migrationScope = app.Services.CreateScope();
                var logger = migrationScope.ServiceProvider.GetRequiredService<ILogger<WebApplication>>();
                var db = migrationScope.ServiceProvider.GetRequiredService<GuapMessengerContext>();

                try
                {
                    var appliedMigrations = db.Database.GetAppliedMigrations().ToList();
                    var currentMigration = appliedMigrations.LastOrDefault();
                    logger.LogInformation("Текущая миграция базы данных: {Migration}", currentMigration ?? "Нет");

                    var pendingMigrations = db.Database.GetPendingMigrations().ToList();

                    if (pendingMigrations.Count == 0)
                    {
                        logger.LogInformation("Нет ожидающих миграций для применения.");
                    }
                    else
                    {
                        logger.LogInformation("Найдено {Count} ожидающих миграций. Применяю...", pendingMigrations.Count);

                        foreach (var migration in pendingMigrations)
                        {
                            logger.LogInformation("Применяю миграцию: {Migration}", migration);
                            db.Database.Migrate(migration);
                            logger.LogInformation("Миграция успешно применена: {Migration}", migration);
                        }

                        logger.LogInformation("Все ожидающие миграции успешно применены.");
                    }

                    var finalMigration = db.Database.GetAppliedMigrations().LastOrDefault();
                    logger.LogInformation("Миграция базы данных завершена. Текущая миграция: {Migration}",
                        finalMigration ?? "Нет");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Ошибка при применении миграций базы данных.");
                    throw;
                }

                return app;
            }
        }
    }
}
