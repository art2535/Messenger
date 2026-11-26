using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Messenger.Web.Services
{
    public sealed class TokenRefresher
    {
        private const string FallbackOidcBase = "https://sso.guap.ru/realms/master/protocol/openid-connect";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly TokenRefreshOptions _options;
        private readonly UserTokenStore _store;
        private readonly ILogger<TokenRefresher> _logger;

        public TokenRefresher(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IOptions<TokenRefreshOptions> options,
            UserTokenStore store,
            ILogger<TokenRefresher> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _options = options.Value;
            _store = store;
            _logger = logger;
        }

        public bool NeedsRefresh(TokenEntry entry)
            => !entry.IsRevoked
               && entry.Tokens.ExpiresAtUtc - DateTime.UtcNow
                  <= TimeSpan.FromSeconds(_options.RefreshBeforeExpirySeconds);

        private string OidcBase
        {
            get
            {
                var instance = _configuration["AzureAd:Instance"]?.TrimEnd('/');
                var realm = _configuration["AzureAd:TenantId"];

                return string.IsNullOrEmpty(instance) || string.IsNullOrEmpty(realm)
                    ? FallbackOidcBase
                    : $"{instance}/{realm}/protocol/openid-connect";
            }
        }

        private string TokenEndpoint => $"{OidcBase}/token";
        private string RevokeEndpoint => $"{OidcBase}/revoke";

        public async Task RefreshIfNeededAsync(TokenEntry entry, CancellationToken cancellationToken)
        {
            if (entry.IsRevoked || !NeedsRefresh(entry))
                return;

            if (_store.IsRevoked(entry.Key, entry.Tokens.RefreshToken))
            {
                DropSession(entry, "tombstone");
                return;
            }

            await entry.RefreshLock.WaitAsync(cancellationToken);
            try
            {
                if (entry.IsRevoked || !NeedsRefresh(entry))
                    return;

                if (_store.IsRevoked(entry.Key, entry.Tokens.RefreshToken))
                {
                    DropSession(entry, "tombstone");
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
                    if (entry.IsRevoked || _store.IsRevoked(entry.Key, current.RefreshToken))
                    {
                        using var docEarly = JsonDocument.Parse(body);
                        var rootEarly = docEarly.RootElement;
                        var newRefreshEarly = rootEarly.TryGetProperty("refresh_token", out var re)
                            ? re.GetString()
                            : null;
                        _logger.LogWarning(
                            "[TokenRefresh] Сессия {Key}: выход во время обновления — новый refresh-токен отзывается",
                            entry.Key);
                        await RevokeRefreshTokenAsync(newRefreshEarly ?? current.RefreshToken, CancellationToken.None);
                        DropSession(entry, "revoked-during-refresh");
                        return;
                    }

                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;

                    var newAccess = root.GetProperty("access_token").GetString();
                    var newRefresh = root.TryGetProperty("refresh_token", out var refreshElement)
                        ? refreshElement.GetString()
                        : null;

                    if (string.IsNullOrEmpty(newAccess))
                    {
                        _logger.LogError("[TokenRefresh] SSO вернул пустой access_token для {Key}", entry.Key);
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
                         or HttpStatusCode.Unauthorized
                         or HttpStatusCode.Forbidden)
                {
                    _logger.LogWarning(
                        "[TokenRefresh] Сессия {Key} окончательно отозвана SSO ({Status}): {Body}",
                        entry.Key, (int)response.StatusCode, Truncate(body, 200));
                    DropSession(entry, "sso-rejected");
                }
                else
                {
                    _logger.LogError(
                        "[TokenRefresh] Временная ошибка обновления {Key}: {Status} - {Body}",
                        entry.Key, (int)response.StatusCode, Truncate(body, 200));
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
                try
                {
                    entry.RefreshLock.Release();
                }
                catch { }
            }
        }

        private void DropSession(TokenEntry entry, string reason)
        {
            entry.IsRevoked = true;
            _store.RevokeSession(entry.Key, entry.Tokens.AccessToken, entry.Tokens.RefreshToken);
            _logger.LogInformation(
                "[TokenRefresh] Сессия {Key} удалена из TokenStore ({Reason})",
                entry.Key, reason);
        }

        private static string Truncate(string? s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s[..max] + "…";
        }

        public async Task<bool> RevokeRefreshTokenAsync(string? refreshToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(refreshToken))
                return false;

            try
            {
                var clientId = _configuration["AzureAd:ClientId"] ?? "messager";
                var clientSecret = _configuration["AzureAd:ClientSecret"];

                var form = new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["token"] = refreshToken,
                    ["token_type_hint"] = "refresh_token"
                };
                if (!string.IsNullOrEmpty(clientSecret))
                    form["client_secret"] = clientSecret;

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));

                var client = _httpClientFactory.CreateClient();
                using var content = new FormUrlEncodedContent(form);
                using var response = await client.PostAsync(RevokeEndpoint, content, timeout.Token);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[TokenRefresh] Refresh-токен отозван в SSO ({Status})", (int)response.StatusCode);
                    return true;
                }

                var body = await response.Content.ReadAsStringAsync(timeout.Token);
                if ((int)response.StatusCode is 400 or 401)
                {
                    _logger.LogDebug("[TokenRefresh] SSO revoke: сессия уже недействительна ({Status})", (int)response.StatusCode);
                    return true;
                }

                _logger.LogWarning("[TokenRefresh] SSO revoke вернул {Status}: {Body}",
                    (int)response.StatusCode, Truncate(body, 200));
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TokenRefresh] Не удалось отозвать refresh-токен в SSO");
                return false;
            }
        }
    }
}