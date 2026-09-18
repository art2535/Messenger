using Messenger.Core.DTOs.UserStatuses;
using Messenger.Core.Hubs;
using Messenger.Web.Helpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Messenger.Web.Pages.Authorization
{
    public class LogoutModel : PageModel
    {
        private readonly ApiHelper _api;
        private readonly ILogger<LogoutModel> _logger;
        private readonly IHubContext<ChatHub> _hubContext;

        public LogoutModel(ApiHelper api, ILogger<LogoutModel> logger, IHubContext<ChatHub> hubContext)
        {
            _api = api;
            _logger = logger;
            _hubContext = hubContext;
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
            var token = await HttpContext.GetTokenAsync("access_token");

            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    var loginResponse = await _api.PatchRawAsync("logins", accessToken: token);
                    if (!loginResponse.IsSuccessStatusCode)
                    {
                        var error = await loginResponse.Content.ReadAsStringAsync();
                        _logger.LogError("Ошибка API при выходе: {StatusCode} - {Error}",
                            loginResponse.StatusCode, error);
                    }

                    var userStatusRequest = new UpdateStatusRequest { Online = false };
                    var statusResponse = await _api.PutRawAsync("userstatuses", userStatusRequest, token);

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
                                _logger.LogInformation("SignalR уведомление о выходе отправлено для пользователя {UserId}", userId);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning("Не удалось отправить SignalR уведомление о выходе: {Message}", ex.Message);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Не удалось связаться с API при выходе");
                }
            }

            HttpContext.Session.Clear();

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            Response.Cookies.Delete(".AspNetCore.Session");

            return RedirectToPage("/Authorization/Authorization");
        }
    }
}