using Microsoft.Extensions.Options;

namespace Messenger.Web.Services
{
    public sealed class TokenRefreshBackgroundService : BackgroundService
    {
        private readonly UserTokenStore _store;
        private readonly TokenRefresher _refresher;
        private readonly TokenRefreshOptions _options;
        private readonly ILogger<TokenRefreshBackgroundService> _logger;

        public TokenRefreshBackgroundService(UserTokenStore store, TokenRefresher refresher,
            IOptions<TokenRefreshOptions> options, ILogger<TokenRefreshBackgroundService> logger)
        {
            _store = store;
            _refresher = refresher;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[TokenRefresh] Фоновый сервис запущен (проверка каждые {Interval} с)",
                _options.CheckIntervalSeconds);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.CheckIntervalSeconds));

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        await RefreshAllAsync(stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "[TokenRefresh] Ошибка цикла обновления");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task RefreshAllAsync(CancellationToken cancellationToken)
        {
            var idleLimit = TimeSpan.FromMinutes(_options.IdleTimeoutMinutes);
            var entries = _store.Snapshot();

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var nearest = entries
                    .Where(e => !e.IsRevoked)
                    .Select(e => (e.Tokens.ExpiresAtUtc - DateTime.UtcNow).TotalSeconds)
                    .DefaultIfEmpty(double.NaN)
                    .Min();

                _logger.LogDebug("[TokenRefresh] Тик: активных сессий {Count}, ближайшее истечение токена через {Seconds:F0} с",
                    entries.Length, nearest);
            }

            foreach (var entry in entries)
            {
                if (entry.IsRevoked)
                {
                    _store.Remove(entry.Key);
                    _logger.LogInformation("[TokenRefresh] Сессия {Key} удалена: отозвана (logout)", entry.Key);
                    continue;
                }

                if (DateTime.UtcNow - entry.LastSeenUtc > idleLimit)
                {
                    _store.Remove(entry.Key);
                    _logger.LogInformation("[TokenRefresh] Сессия {Key} удалена из хранилища: нет запросов более {Minutes} мин",
                        entry.Key, _options.IdleTimeoutMinutes);
                    continue;
                }

                await _refresher.RefreshIfNeededAsync(entry, cancellationToken);
            }
        }
    }
}