using Messenger.Web.Helpers;
using Microsoft.Extensions.Options;

namespace Messenger.Web.Services
{
    public sealed class TokenRefreshBackgroundService : BackgroundService
    {
        private readonly UserTokenStore _store;
        private readonly TokenRefresher _refresher;
        private readonly TokenRefreshOptions _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TokenRefreshBackgroundService> _logger;

        public TokenRefreshBackgroundService(UserTokenStore store, TokenRefresher refresher,
            IOptions<TokenRefreshOptions> options, IServiceScopeFactory scopeFactory,
            ILogger<TokenRefreshBackgroundService> logger)
        {
            _store = store;
            _refresher = refresher;
            _options = options.Value;
            _scopeFactory = scopeFactory;
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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        private async Task RefreshAllAsync(CancellationToken cancellationToken)
        {
            var idleLimit = TimeSpan.FromMinutes(_options.IdleTimeoutMinutes);
            _store.PurgeExpiredTombstones();
            var entries = _store.Snapshot();

            foreach (var entry in entries)
            {
                bool needsDbExpiration = false;

                if (entry.IsRevoked)
                {
                    _store.Remove(entry.Key, entry);
                    _logger.LogInformation("[TokenRefresh] Сессия {Key} удалена: отозвана (logout)", entry.Key);
                    needsDbExpiration = true;
                }
                else if (DateTime.UtcNow - entry.LastSeenUtc > idleLimit)
                {
                    _store.Remove(entry.Key);
                    _logger.LogInformation("[TokenRefresh] Сессия {Key} удалена из хранилища: нет запросов более {Minutes} мин",
                        entry.Key, _options.IdleTimeoutMinutes);
                    needsDbExpiration = true;
                }
                else
                {
                    var sessionActive = await IsLoginSessionActiveAsync(entry.Key, cancellationToken);
                    if (sessionActive == false)
                    {
                        entry.IsRevoked = true;
                        _store.RevokeSession(entry.Key, entry.Tokens.AccessToken, entry.Tokens.RefreshToken);
                        _logger.LogInformation(
                            "[TokenRefresh] Сессия {Key} удалена: login отозван в БД (завершение сессии / idle)",
                            entry.Key);
                        needsDbExpiration = true;
                    }
                    else
                    {
                        await _refresher.RefreshIfNeededAsync(entry, cancellationToken);

                        if (entry.IsRevoked)
                        {
                            _store.RevokeSession(entry.Key, entry.Tokens.AccessToken, entry.Tokens.RefreshToken);
                            _logger.LogInformation(
                                "[TokenRefresh] Сессия {Key} снята с фонового обновления после отзыва",
                                entry.Key);
                            needsDbExpiration = true;
                        }
                    }
                }

                if (needsDbExpiration)
                {
                    await ExpireSessionInDbAsync(entry.Key, cancellationToken);
                }
            }
        }

        private async Task ExpireSessionInDbAsync(string sessionKey, CancellationToken ct)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var api = scope.ServiceProvider.GetRequiredService<ApiHelper>();

                var path = $"login/session/{Uri.EscapeDataString(sessionKey)}/expire";
                using var res = await api.PostRawAsync<object>(path, new { }, accessToken: null, ct);

                if (!res.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[TokenRefresh] API вернул {Status} при попытке завершить сессию {Key}",
                        res.StatusCode, sessionKey);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[TokenRefresh] Не удалось уведомить API об истечении сессии {Key}", sessionKey);
            }
        }

        private async Task<bool?> IsLoginSessionActiveAsync(string sessionKey, CancellationToken ct)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var api = scope.ServiceProvider.GetRequiredService<ApiHelper>();

                var path = "login/session/" + Uri.EscapeDataString(sessionKey) + "/active";
                using var res = await api.GetRawAsync(path, accessToken: null, ct);
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[TokenRefresh] Не удалось проверить активность сессии {Key}", sessionKey);
                return null;
            }
        }
    }
}