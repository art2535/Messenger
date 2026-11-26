using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Persistence
{
    public sealed class DatabaseMigrationHostedService(
        IServiceScopeFactory scopeFactory,
        DatabaseMigrationStatus migrationStatus,
        ILogger<DatabaseMigrationHostedService> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            migrationStatus.MarkRunning();
            logger.LogInformation("Начинаю фоновую инициализацию базы данных и применение миграций.");

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<GuapMessengerContext>();

                var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync(stoppingToken)).ToList();
                var currentMigration = appliedMigrations.LastOrDefault();
                logger.LogInformation("Текущая миграция базы данных: {Migration}", currentMigration ?? "Нет");

                var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(stoppingToken)).ToList();

                if (pendingMigrations.Count == 0)
                {
                    logger.LogInformation("Нет ожидающих миграций для применения.");
                }
                else
                {
                    logger.LogInformation("Найдено {Count} ожидающих миграций. Применяю...", pendingMigrations.Count);

                    foreach (var migration in pendingMigrations)
                    {
                        stoppingToken.ThrowIfCancellationRequested();
                        logger.LogInformation("Применяю миграцию: {Migration}", migration);
                        await db.Database.MigrateAsync(migration, stoppingToken);
                        logger.LogInformation("Миграция успешно применена: {Migration}", migration);
                    }

                    logger.LogInformation("Все ожидающие миграции успешно применены.");
                }

                var finalMigration = (await db.Database.GetAppliedMigrationsAsync(stoppingToken)).LastOrDefault();
                logger.LogInformation("Миграция базы данных завершена. Текущая миграция: {Migration}",
                    finalMigration ?? "Нет");

                migrationStatus.MarkCompleted();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Фоновая инициализация базы данных отменена при остановке приложения.");
                throw;
            }
            catch (Exception exception)
            {
                migrationStatus.MarkFailed(exception);
                logger.LogCritical(exception, "Не удалось выполнить фоновую инициализацию базы данных.");
            }
        }
    }
}
