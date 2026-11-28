using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Messenger.API.Services
{
    public sealed class LogRetentionOptions
    {
        public const string SectionName = "LogRetention";
        public bool Enabled { get; set; } = true;
        [Range(1, 3650)]
        public int RetentionDays { get; set; } = 30;
        [Range(1, 720)]
        public int RunIntervalHours { get; set; } = 24;
        [Range(100, 100000)]
        public int BatchSize { get; set; } = 5000;
    }

    public sealed class LogRetentionService(
        IServiceScopeFactory scopeFactory,
        IOptions<LogRetentionOptions> options,
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

            logger.LogInformation("Фоновая очистка Logs запущена: интервал {Interval} ч., срок хранения {Retention} дн.",
                _options.RunIntervalHours, _options.RetentionDays);

            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }

            using var timer = new PeriodicTimer(TimeSpan.FromHours(_options.RunIntervalHours));
            do { await CleanupAsync(stoppingToken); }
            while (await WaitNextAsync(timer, stoppingToken));
        }

        private static async Task<bool> WaitNextAsync(PeriodicTimer timer, CancellationToken ct)
        {
            try { return await timer.WaitForNextTickAsync(ct); }
            catch (OperationCanceledException) { return false; }
        }

        private async Task CleanupAsync(CancellationToken ct)
        {
            var threshold = DateTime.UtcNow.AddDays(-_options.RetentionDays);
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
                        LIMIT {_options.BatchSize})
                    """, ct);
                    total += deleted;
                    if (deleted == _options.BatchSize)
                        await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
                }
                while (deleted == _options.BatchSize && !ct.IsCancellationRequested);

                logger.LogInformation("Очистка логов завершена: удалено {Count} записей старше {Threshold:u} ({Days} дн.)",
                    total, threshold, _options.RetentionDays);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { logger.LogError(ex, "Ошибка очистки логов (удалено до ошибки: {Count})", total); }
        }
    }
}