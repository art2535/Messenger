using Messenger.Core.DTOs.UserStatuses;
using Messenger.Core.Hubs;
using Messenger.Web.Helpers;
using Messenger.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Messenger.Web.Pages.Authorization
{
    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class LogoutModel : PageModel
    {
        private readonly ApiHelper _api;
        private readonly ILogger<LogoutModel> _logger;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly UserTokenStore _tokenStore;
        private readonly TokenRefresher _tokenRefresher;

        public LogoutModel(ApiHelper api, ILogger<LogoutModel> logger, IHubContext<ChatHub> hubContext,
            UserTokenStore tokenStore, TokenRefresher tokenRefresher)
        {
            _api = api;
            _logger = logger;
            _hubContext = hubContext;
            _tokenStore = tokenStore;
            _tokenRefresher = tokenRefresher;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            return await PerformLogoutAsync();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            return await PerformLogoutAsync();
        }

        private async Task<IActionResult> PerformLogoutAsync()
        {
            var accessToken = await HttpContext.GetTokenAsync("access_token");
            var refreshToken = await HttpContext.GetTokenAsync("refresh_token");

            var closedSessions = await StopTokenRefreshAsync(accessToken, refreshToken);

            if (!string.IsNullOrEmpty(accessToken))
            {
                try
                {
                    var loginResponse = await _api.PatchRawAsync("logins", accessToken: accessToken);
                    if (!loginResponse.IsSuccessStatusCode)
                    {
                        var error = await loginResponse.Content.ReadAsStringAsync();
                        _logger.LogWarning("Ошибка API при выходе (logins): {StatusCode} - {Error}",
                            loginResponse.StatusCode, error);
                    }

                    var userStatusRequest = new UpdateStatusRequest { Online = false };
                    var statusResponse = await _api.PutRawAsync("userstatuses", userStatusRequest, accessToken);

                    if (statusResponse.IsSuccessStatusCode)
                    {
                        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                     ?? User.FindFirst("sub")?.Value;

                        if (Guid.TryParse(userIdStr, out Guid userId))
                        {
                            try
                            {
                                var payload = new
                                {
                                    userId = userId.ToString(),
                                    isOnline = false,
                                    lastActivity = DateTime.UtcNow
                                };
                                await _hubContext.Clients.All.SendAsync("UserOnlineStatusChanged", payload);
                            }
                            catch (Exception hubEx)
                            {
                                _logger.LogWarning(hubEx, "Не удалось разослать offline-статус через SignalR");
                            }
                        }
                    }
                    else
                    {
                        var error = await statusResponse.Content.ReadAsStringAsync();
                        _logger.LogWarning("Ошибка API при выходе (userstatuses): {StatusCode} - {Error}",
                            statusResponse.StatusCode, error);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Не удалось связаться с API при выходе");
                }
            }

            await RevokeTokensInSsoAsync(refreshToken, closedSessions);

            try
            {
                HttpContext.Session.Clear();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось очистить Session при выходе");
            }

            try
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка SignOut Cookie");
            }

            DeleteCookie(".AspNetCore.Session");
            DeleteCookie(".GuapMessenger.Cookie");
            DeleteCookie(".AspNetCore.Antiforgery");

            return RedirectToPage("/Authorization/Authorization", new { loggedOut = true });
        }

        private async Task<IReadOnlyList<TokenEntry>> StopTokenRefreshAsync(string? accessToken, string? refreshToken)
        {
            try
            {
                string? storeKey = null;
                var auth = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                if (auth.Succeeded && auth.Properties != null)
                {
                    auth.Properties.Items.TryGetValue(UserTokenStore.StoreKeyItem, out storeKey);
                }

                var closed = _tokenStore.RevokeSession(storeKey, accessToken, refreshToken);

                foreach (var entry in closed)
                {
                    if (await entry.RefreshLock.WaitAsync(TimeSpan.FromSeconds(10)))
                        entry.RefreshLock.Release();
                }

                _logger.LogInformation("[Logout] TokenStore: key={Key} closedSessions={Count}",
                    storeKey ?? "(null)", closed.Count);

                return closed;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Logout] Не удалось остановить фоновое обновление токенов");
                return Array.Empty<TokenEntry>();
            }
        }

        private async Task RevokeTokensInSsoAsync(string? cookieRefreshToken, IReadOnlyList<TokenEntry> closedSessions)
        {
            var refreshTokens = new List<string>();
            foreach (var entry in closedSessions)
            {
                var current = entry.Tokens.RefreshToken;
                if (!string.IsNullOrEmpty(current) && !refreshTokens.Contains(current))
                    refreshTokens.Add(current);
            }
            if (!string.IsNullOrEmpty(cookieRefreshToken) && !refreshTokens.Contains(cookieRefreshToken))
                refreshTokens.Add(cookieRefreshToken);

            if (refreshTokens.Count == 0)
            {
                _logger.LogInformation("[Logout] Нет refresh-токенов для отзыва в SSO");
                return;
            }

            foreach (var token in refreshTokens)
            {
                var revoked = await _tokenRefresher.RevokeRefreshTokenAsync(token, CancellationToken.None);
                if (!revoked)
                    _logger.LogWarning("[Logout] Refresh-токен не удалось отозвать в SSO");
            }
        }

        private void DeleteCookie(string name)
        {
            Response.Cookies.Delete(name, new CookieOptions
            {
                Path = "/",
                Secure = true,
                SameSite = SameSiteMode.Lax,
                HttpOnly = true
            });
        }
    }
}
