using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Messenger.Infrastructure.Persistence
{
    public sealed class DatabaseMigrationHealthCheck(DatabaseMigrationStatus migrationStatus) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(migrationStatus.State switch
            {
                MigrationState.Completed => HealthCheckResult.Healthy("Миграции базы данных применены."),
                MigrationState.Pending => HealthCheckResult.Unhealthy("Ожидается запуск миграций базы данных."),
                MigrationState.Running => HealthCheckResult.Unhealthy("Выполняется инициализация базы данных."),
                MigrationState.Failed => HealthCheckResult.Unhealthy(
                    "Ошибка при инициализации базы данных.",
                    migrationStatus.Exception),
                _ => HealthCheckResult.Unhealthy("Неизвестное состояние миграций базы данных.")
            });
        }
    }
}
