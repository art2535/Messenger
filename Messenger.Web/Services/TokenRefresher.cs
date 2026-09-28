using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Messenger.Web.Services
{
    /// <summary>
    /// Обновление токенов через refresh_token в SSO. Используется и фоновым сервисом,
    /// и (как запасной путь) при проверке cookie.
    /// </summary>
    public sealed class TokenRefresher
    {
        private const string FallbackTokenEndpoint = "https://sso.guap.ru/realms/master/protocol/openid-connect/token";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly TokenRefreshOptions _options;
        private readonly ILogger<TokenRefresher> _logger;

        public TokenRefresher(IHttpClientFactory httpClientFactory, IConfiguration configuration,
            IOptions<TokenRefreshOptions> options, ILogger<TokenRefresher> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _options = options.Value;
            _logger = logger;
        }

        public bool NeedsRefresh(TokenEntry entry)
            => entry.Tokens.ExpiresAtUtc - DateTime.UtcNow <= TimeSpan.FromSeconds(_options.RefreshBeforeExpirySeconds);

        private string TokenEndpoint
        {
            get
            {
                var instance = _configuration["AzureAd:Instance"]?.TrimEnd('/');
                var realm = _configuration["AzureAd:TenantId"];

                return string.IsNullOrEmpty(instance) || string.IsNullOrEmpty(realm)
                    ? FallbackTokenEndpoint
                    : $"{instance}/{realm}/protocol/openid-connect/token";
            }
        }

        /// <summary>Обновляет токены, если они скоро истекут. Ошибки не выбрасывает — только логирует.</summary>
        public async Task RefreshIfNeededAsync(TokenEntry entry, CancellationToken cancellationToken)
        {
            if (entry.IsRevoked || !NeedsRefresh(entry))
            {
                return;
            }

            await entry.RefreshLock.WaitAsync(cancellationToken);
            try
            {
                // Пока ждали блокировку, токен мог уже обновить другой поток.
                if (entry.IsRevoked || !NeedsRefresh(entry))
                {
                    return;
                }

                var current = entry.Tokens;

                _logger.LogInformation(
                    "[TokenRefresh] Сессия {Key}: access-токен истекает через {Seconds:F0} с — запрашиваем новый у SSO",
                    entry.Key, (current.ExpiresAtUtc - DateTime.UtcNow).TotalSeconds);

                var form = new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = current.RefreshToken,
                    ["client_id"] = _configuration["AzureAd:ClientId"] ?? "messager",
                    ["client_secret"] = _configuration["AzureAd:ClientSecret"] ?? ""
                };

                var client = _httpClientFactory.CreateClient();
                using var response = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;

                    var newAccess = root.GetProperty("access_token").GetString();
                    var newRefresh = root.TryGetProperty("refresh_token", out var refreshElement)
                        ? refreshElement.GetString()
                        : null;

                    if (string.IsNullOrEmpty(newAccess))
                    {
                        _logger.LogError("[TokenRefresh] SSO вернул пустой access_token");
                        return;
                    }

                    var lifetime = root.TryGetProperty("expires_in", out var expiresElement)
                                   && expiresElement.TryGetInt32(out var seconds)
                        ? TimeSpan.FromSeconds(seconds)
                        : TokenSet.DefaultLifetime;

                    var updated = TokenSet.Create(newAccess, newRefresh ?? current.RefreshToken, lifetime);
                    entry.Tokens = updated;

                    _logger.LogInformation(
                        "[TokenRefresh] Сессия {Key}: токен ОБНОВЛЁН, новый истекает в {ExpiresAt:HH:mm:ss} UTC (через {Seconds:F0} с), refresh-токен: {Rotated}",
                        entry.Key,
                        updated.ExpiresAtUtc,
                        (updated.ExpiresAtUtc - DateTime.UtcNow).TotalSeconds,
                        updated.RefreshToken != current.RefreshToken ? "заменён" : "прежний");
                }
                else if (response.StatusCode is HttpStatusCode.BadRequest
                         or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    // Keycloak на просроченный/отозванный refresh_token отвечает 400 invalid_grant.
                    entry.IsRevoked = true;
                    _logger.LogWarning("[TokenRefresh] Refresh-токен сессии {Key} отклонён ({Status}): {Body}",
                        entry.Key, (int)response.StatusCode, body);
                }
                else
                {
                    // Временный сбой SSO (5xx и т.п.) — попробуем на следующем тике.
                    _logger.LogError("[TokenRefresh] Ошибка обновления: {Status} - {Body}",
                        (int)response.StatusCode, body);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TokenRefresh] Ошибка при обновлении токена сессии {Key}", entry.Key);
            }
            finally
            {
                entry.RefreshLock.Release();
            }
        }
    }
}