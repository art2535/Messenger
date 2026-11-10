using Microsoft.Extensions.Options;

namespace Messenger.Web.Services
{
    public sealed class TokenRefreshBackgroundService : BackgroundService
    {
        private readonly UserTokenStore _store;
        private readonly TokenRefresher _refresher;
        private readonly TokenRefreshOptions _options;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TokenRefreshBackgroundService> _logger;

        public TokenRefreshBackgroundService(
            UserTokenStore store,
            TokenRefresher refresher,
            IOptions<TokenRefreshOptions> options,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<TokenRefreshBackgroundService> logger)
        {
            _store = store;
            _refresher = refresher;
            _options = options.Value;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
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
            _store.PurgeExpiredTombstones();
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

                var sessionActive = await IsLoginSessionActiveAsync(entry.Key, cancellationToken);
                if (sessionActive == false)
                {
                    entry.IsRevoked = true;
                    _store.RevokeSession(entry.Key, entry.Tokens.AccessToken, entry.Tokens.RefreshToken);
                    _logger.LogInformation(
                        "[TokenRefresh] Сессия {Key} удалена: login отозван в БД (завершение сессии / idle)",
                        entry.Key);
                    continue;
                }

                await _refresher.RefreshIfNeededAsync(entry, cancellationToken);
            }
        }

        private async Task<bool?> IsLoginSessionActiveAsync(string sessionKey, CancellationToken ct)
        {
            try
            {
                var apiBase = (_configuration["URL:API:HTTPS"] ?? string.Empty).TrimEnd('/');

                if (string.IsNullOrEmpty(apiBase))
                    return null;

                if (!apiBase.Contains("/api/", StringComparison.OrdinalIgnoreCase))
                    apiBase += "/api/v1.0";

                var url = $"{apiBase}/logins/session/{Uri.EscapeDataString(sessionKey)}/active";
                var client = _httpClientFactory.CreateClient();
                using var res = await client.GetAsync(url, ct);
                if (!res.IsSuccessStatusCode)
                    return null;

                await using var stream = await res.Content.ReadAsStreamAsync(ct);
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = doc.RootElement;
                if (root.TryGetProperty("active", out var a))
                    return a.GetBoolean();
                if (root.TryGetProperty("Active", out var a2))
                    return a2.GetBoolean();
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[TokenRefresh] Не удалось проверить активность сессии {Key}", sessionKey);
                return null;
            }
        }
    }
}