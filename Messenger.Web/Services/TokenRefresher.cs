using System.Net;
using System.Text.Json;
using Messenger.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Messenger.Web.Services
{
    public sealed class TokenRefresher
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IdentityProviderOptions _identityProvider;
        private readonly TokenRefreshOptions _options;
        private readonly UserTokenStore _store;
        private readonly ILogger<TokenRefresher> _logger;

        public TokenRefresher(
            IHttpClientFactory httpClientFactory,
            IOptions<IdentityProviderOptions> identityProvider,
            IOptions<TokenRefreshOptions> options,
            UserTokenStore store,
            ILogger<TokenRefresher> logger)
        {
            _httpClientFactory = httpClientFactory;
            _identityProvider = identityProvider.Value;
            _options = options.Value;
            _store = store;
            _logger = logger;
        }

        private string TokenEndpoint => $"{_identityProvider.OpenIdConnectBase}/token";
        private string RevokeEndpoint => $"{_identityProvider.OpenIdConnectBase}/revoke";

        public bool NeedsRefresh(TokenEntry entry)
            => !entry.IsRevoked
               && entry.Tokens.ExpiresAtUtc - DateTime.UtcNow
                  <= TimeSpan.FromSeconds(_options.RefreshBeforeExpirySeconds);

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
                _logger.LogInformation("[TokenRefresh] Сессия {Key}: access-токен истекает через {Seconds:F0} с — запрашиваем новый у SSO",
                    entry.Key, (current.ExpiresAtUtc - DateTime.UtcNow).TotalSeconds);

                var form = new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = current.RefreshToken,
                    ["client_id"] = _identityProvider.ClientId,
                    ["client_secret"] = _identityProvider.ClientSecret!
                };

                var client = _httpClientFactory.CreateClient();
                using var response = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    if (entry.IsRevoked || _store.IsRevoked(entry.Key, current.RefreshToken))
                    {
                        using var earlyDoc = JsonDocument.Parse(body);
                        var newRefresh = earlyDoc.RootElement.TryGetProperty("refresh_token", out var refreshElement)
                            ? refreshElement.GetString()
                            : null;
                        _logger.LogWarning("[TokenRefresh] Сессия {Key}: выход во время обновления — новый refresh-токен отзывается", entry.Key);
                        await RevokeRefreshTokenAsync(newRefresh ?? current.RefreshToken, CancellationToken.None);
                        DropSession(entry, "revoked-during-refresh");
                        return;
                    }

                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    var newAccess = root.GetProperty("access_token").GetString();
                    var newRefreshToken = root.TryGetProperty("refresh_token", out var refreshTokenElement)
                        ? refreshTokenElement.GetString()
                        : null;
                    if (string.IsNullOrEmpty(newAccess))
                    {
                        _logger.LogError("[TokenRefresh] SSO вернул пустой access_token для {Key}", entry.Key);
                        return;
                    }

                    var lifetime = root.TryGetProperty("expires_in", out var expiresElement) && expiresElement.TryGetInt32(out var seconds)
                        ? TimeSpan.FromSeconds(seconds)
                        : TokenSet.DefaultLifetime;
                    var updated = TokenSet.Create(newAccess, newRefreshToken ?? current.RefreshToken, lifetime);
                    entry.Tokens = updated;
                    _logger.LogInformation("[TokenRefresh] Сессия {Key}: токен обновлён, истекает в {ExpiresAt:HH:mm:ss} UTC; refresh-токен: {Rotated}",
                        entry.Key, updated.ExpiresAtUtc, updated.RefreshToken != current.RefreshToken ? "заменён" : "прежний");
                }
                else if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    _logger.LogWarning("[TokenRefresh] Сессия {Key} окончательно отозвана SSO ({Status}): {Body}",
                        entry.Key, (int)response.StatusCode, Truncate(body, 200));
                    DropSession(entry, "sso-rejected");
                }
                else
                {
                    _logger.LogError("[TokenRefresh] Временная ошибка обновления {Key}: {Status} - {Body}",
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
                try { entry.RefreshLock.Release(); }
                catch (SemaphoreFullException) { }
            }
        }

        private void DropSession(TokenEntry entry, string reason)
        {
            entry.IsRevoked = true;
            _store.RevokeSession(entry.Key, entry.Tokens.AccessToken, entry.Tokens.RefreshToken);
            _logger.LogInformation("[TokenRefresh] Сессия {Key} удалена из TokenStore ({Reason})", entry.Key, reason);
        }

        private static string Truncate(string? value, int max)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= max ? value : value[..max] + "…";
        }

        public async Task<bool> RevokeRefreshTokenAsync(string? refreshToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(refreshToken))
                return false;

            try
            {
                var form = new Dictionary<string, string>
                {
                    ["client_id"] = _identityProvider.ClientId,
                    ["client_secret"] = _identityProvider.ClientSecret!,
                    ["token"] = refreshToken,
                    ["token_type_hint"] = "refresh_token"
                };

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

                _logger.LogWarning("[TokenRefresh] SSO revoke вернул {Status}: {Body}", (int)response.StatusCode, Truncate(body, 200));
                return false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TokenRefresh] Не удалось отозвать refresh-токен в SSO");
                return false;
            }
        }
    }
}