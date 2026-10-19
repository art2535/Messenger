using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Messenger.API.Services
{
    public sealed class LogRetentionOptions
    {
        public const string SectionName = "LogRetention";
        public bool Enabled { get; set; } = true;
        public int RetentionDays { get; set; } = 30;
        public int RunIntervalHours { get; set; } = 24;
        public int BatchSize { get; set; } = 5000;
    }

    public sealed class LogRetentionService(IServiceScopeFactory scopeFactory, IOptions<LogRetentionOptions> options,
        ILogger<LogRetentionService> logger) : BackgroundService
    {
        private readonly LogRetentionOptions _options = options.Value;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                logger.LogInformation("Очистка логов отключена (LogRetention:Enabled = false)");
                return;
            }

            logger.LogInformation($"Фоновый процесс очистки Logs запущен: каждые {_options.RetentionDays} дней");

            var days = Math.Max(1, _options.RetentionDays);
            var interval = TimeSpan.FromHours(Math.Max(1, _options.RunIntervalHours));

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            using var timer = new PeriodicTimer(interval);
            do
            {
                await CleanupAsync(days, stoppingToken);
            }
            while (await WaitNextAsync(timer, stoppingToken));
        }

        private static async Task<bool> WaitNextAsync(PeriodicTimer timer, CancellationToken ct)
        {
            try
            {
                return await timer.WaitForNextTickAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private async Task CleanupAsync(int retentionDays, CancellationToken ct)
        {
            var threshold = DateTime.UtcNow.AddDays(-retentionDays);
            var batchSize = Math.Max(100, _options.BatchSize);
            var total = 0L;

            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<GuapMessengerContext>();

                int deleted;
                do
                {
                    deleted = await db.Database.ExecuteSqlInterpolatedAsync(
                        $"""
                    DELETE FROM logging."Logs"
                    WHERE "Id" IN (
                        SELECT "Id" FROM logging."Logs"
                        WHERE "Timestamp" < {threshold}
                        LIMIT {batchSize})
                    """, ct);

                    total += deleted;

                    if (deleted == batchSize)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
                    }
                }
                while (deleted == batchSize && !ct.IsCancellationRequested);

                logger.LogInformation(
                    "Очистка логов: удалено {Count} записей старше {Threshold:u} ({Days} дн.)",
                    total, threshold, retentionDays);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка очистки логов (удалено до ошибки: {Count})", total);
            }
        }
    }
}
