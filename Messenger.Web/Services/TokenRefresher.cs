using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Messenger.Web.Services
{
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

        public async Task RefreshIfNeededAsync(TokenEntry entry, CancellationToken cancellationToken)
        {
            if (entry.IsRevoked || !NeedsRefresh(entry))
            {
                return;
            }

            await entry.RefreshLock.WaitAsync(cancellationToken);
            try
            {
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
                    entry.IsRevoked = true;
                    _logger.LogWarning("[TokenRefresh] Refresh-токен сессии {Key} отклонён ({Status}): {Body}",
                        entry.Key, (int)response.StatusCode, body);
                }
                else
                {
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
        public async Task RevokeRefreshTokenAsync(string? refreshToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(refreshToken))
                return;

            try
            {
                var clientId = _configuration["AzureAd:ClientId"] ?? "messager";
                var clientSecret = _configuration["AzureAd:ClientSecret"];

                var revokeUrl = TokenEndpoint.Replace("/token", "/revoke");
                var form = new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["token"] = refreshToken,
                    ["token_type_hint"] = "refresh_token"
                };
                if (!string.IsNullOrEmpty(clientSecret))
                    form["client_secret"] = clientSecret;

                var client = _httpClientFactory.CreateClient();
                using var content = new FormUrlEncodedContent(form);
                using var response = await client.PostAsync(revokeUrl, content, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[TokenRefresh] Refresh-токен отозван в SSO ({Status})", (int)response.StatusCode);
                }
                else
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("[TokenRefresh] SSO revoke вернул {Status}: {Body}",
                        (int)response.StatusCode, body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TokenRefresh] Не удалось отозвать refresh-токен в SSO");
            }
        }
    }
}