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

namespace Messenger.Web.Pages
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

        public IActionResult OnGet()
        {
            return RedirectToPage("/Login/Index");
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

            var api = _api;
            var hub = _hubContext;
            var logger = _logger;
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? User.FindFirst("sub")?.Value;
            var tokenRefresher = _tokenRefresher;
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(accessToken))
                    {
                        try { await api.PatchRawAsync("logins", accessToken: accessToken); } catch { }
                        try
                        {
                            var statusResponse = await api.PutRawAsync("userstatuses", new UpdateStatusRequest { Online = false }, accessToken);
                            if (statusResponse.IsSuccessStatusCode && Guid.TryParse(userIdStr, out Guid userId))
                            {
                                var payload = new { userId = userId.ToString(), isOnline = false, lastActivity = DateTime.UtcNow };
                                await hub.Clients.Group($"User_{userId}").SendAsync("UserOnlineStatusChanged", payload);
                            }
                        }
                        catch (Exception ex) { logger.LogWarning(ex, "Фоновый offline-статус при выходе"); }
                    }

                    var refreshTokens = new List<string>();
                    foreach (var entry in closedSessions)
                    {
                        var current = entry.Tokens.RefreshToken;
                        if (!string.IsNullOrEmpty(current) && !refreshTokens.Contains(current))
                            refreshTokens.Add(current);
                    }
                    if (!string.IsNullOrEmpty(refreshToken) && !refreshTokens.Contains(refreshToken))
                        refreshTokens.Add(refreshToken);
                    foreach (var token in refreshTokens)
                    {
                        try { await tokenRefresher.RevokeRefreshTokenAsync(token, CancellationToken.None); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Фоновая очистка при выходе");
                }
            });

            try { HttpContext.Session.Clear(); } catch { }

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

            return Redirect("/login");
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
