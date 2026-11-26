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
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                migrationStatus.MarkRunning();
                logger.LogInformation("Начинаю фоновую инициализацию базы данных и применение миграций.");

                try
                {
                    await ApplyMigrationsAsync(stoppingToken);
                    migrationStatus.MarkCompleted();
                    return;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("Фоновая инициализация базы данных отменена при остановке приложения.");
                    throw;
                }
                catch (Exception exception)
                {
                    migrationStatus.MarkFailed(exception);
                    logger.LogError(exception,
                        "Не удалось выполнить миграции базы данных. Повторная попытка через {DelaySeconds} секунд.",
                        RetryDelay.TotalSeconds);

                    await Task.Delay(RetryDelay, stoppingToken);
                }
            }
        }

        private async Task ApplyMigrationsAsync(CancellationToken cancellationToken)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GuapMessengerContext>();

            var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
            var currentMigration = appliedMigrations.LastOrDefault();
            logger.LogInformation("Текущая миграция базы данных: {Migration}", currentMigration ?? "Нет");

            var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

            if (pendingMigrations.Count == 0)
            {
                logger.LogInformation("Нет ожидающих миграций для применения.");
            }
            else
            {
                logger.LogInformation("Найдено {Count} ожидающих миграций. Применяю...", pendingMigrations.Count);

                foreach (var migration in pendingMigrations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    logger.LogInformation("Применяю миграцию: {Migration}", migration);
                    await db.Database.MigrateAsync(migration, cancellationToken);
                    logger.LogInformation("Миграция успешно применена: {Migration}", migration);
                }

                logger.LogInformation("Все ожидающие миграции успешно применены.");
            }

            var finalMigration = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).LastOrDefault();
            logger.LogInformation("Миграция базы данных завершена. Текущая миграция: {Migration}",
                finalMigration ?? "Нет");
        }
    }
}
