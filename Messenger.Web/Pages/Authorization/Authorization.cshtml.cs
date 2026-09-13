using Messenger.Core.DTOs.Auth;
using Messenger.Core.DTOs.Logins;
using Messenger.Core.DTOs.UserStatuses;
using Messenger.Core.Hubs;
using Messenger.Web.Helpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Claims;

namespace Messenger.Web.Pages.Authorization
{
    public class AuthorizationModel : PageModel
    {
        private readonly ApiHelper _api;
        private readonly ILogger<AuthorizationModel> _logger;
        private readonly IHubContext<ChatHub> _hubContext;

        public string ErrorMessage { get; private set; } = string.Empty;

        public AuthorizationModel(ApiHelper api, ILogger<AuthorizationModel> logger, IHubContext<ChatHub> hubContext)
        {
            _api = api;
            _logger = logger;
            _hubContext = hubContext;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                var accessToken = await HttpContext.GetTokenAsync("access_token");

                if (!string.IsNullOrEmpty(accessToken))
                {
                    var loginRequest = new CreateLoginRequest
                    {
                        Token = accessToken,
                        IpAddress = GetLocalIPv4()
                    };

                    var response = await LoginAsync(loginRequest);
                    if (!response.IsSuccessStatusCode)
                    {
                        ErrorMessage = "Ошибка записи входа в аккаунт";
                        _logger.LogError("Ошибка записи входа в аккаунт при авто-редиректе");
                        return Page();
                    }

                    await SendToSignalRAsync(accessToken, new UpdateStatusRequest { Online = true });
                    HttpContext.Session.SetString("ACCESS_TOKEN", accessToken);
                }

                return RedirectToPage("/Account/Chats", new { tokenSaved = true });
            }

            return Page();
        }

        public async Task<IActionResult> OnGetEtaLoginAsync()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                var accessToken = await HttpContext.GetTokenAsync("access_token");

                if (!string.IsNullOrEmpty(accessToken))
                {
                    var loginRequest = new CreateLoginRequest
                    {
                        Token = accessToken,
                        IpAddress = GetLocalIPv4()
                    };

                    var response = await LoginAsync(loginRequest);
                    if (!response.IsSuccessStatusCode)
                    {
                        ErrorMessage = "Ошибка записи входа в аккаунт";
                        _logger.LogError("Ошибка записи входа в аккаунт");
                        return Page();
                    }

                    await SendToSignalRAsync(accessToken, new UpdateStatusRequest { Online = true });

                    HttpContext.Session.SetString("ACCESS_TOKEN", accessToken!);
                }
                
                return RedirectToPage("/Account/Chats", new { tokenSaved = true });
            }

            var properties = new AuthenticationProperties
            {
                RedirectUri = Url.Page("/Authorization/Authorization", "Callback")
            };

            return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
        }

        public async Task<IActionResult> OnGetCallbackAsync()
        {
            var externalId = User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

            if (string.IsNullOrEmpty(externalId))
            {
                ErrorMessage = "Нет externalId";
                return RedirectToPage("/Authorization/Authorization", new { error = "Нет externalId" });
            }

            var firstName = User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname")?.Value ?? "ЕТА";
            var lastName = User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname")?.Value ?? "Пользователь";
            var email = User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")?.Value ?? "";

            try
            {
                var accessToken = await HttpContext.GetTokenAsync("access_token");
                if (string.IsNullOrEmpty(accessToken))
                {
                    _logger.LogError("Не удалось получить access token после авторизации");
                    ErrorMessage = "Не удалось получить access token";
                    return Page();
                }

                var request = new LoginEtaRequest
                {
                    ExternalId = externalId,
                    Email = email,
                    FirstName = firstName,
                    LastName = lastName,
                    MiddleName = "",
                    IpAddress = GetLocalIPv4(),
                    FakePasswordForInternalUse = $"external_{externalId.Substring(0, 8)}"
                };

                var authResponse = await _api.PostRawAsync("authorization/external/callback", request, accessToken);

                if (!authResponse.IsSuccessStatusCode)
                {
                    var error = await authResponse.Content.ReadAsStringAsync();
                    _logger.LogError("Ошибка внешней авторизации: {StatusCode} - {Error}", authResponse.StatusCode, error);
                    ErrorMessage = "Ошибка авторизации в системе";
                    return Page();
                }

                var loginRequest = new CreateLoginRequest
                {
                    Token = accessToken,
                    IpAddress = GetLocalIPv4()
                };

                var loginResponse = await LoginAsync(loginRequest);
                if (!loginResponse.IsSuccessStatusCode)
                {
                    var error = await loginResponse.Content.ReadAsStringAsync();
                    _logger.LogWarning("Ошибка записи входа: {StatusCode} - {Error}",
                        loginResponse.StatusCode, error);
                }

                await SendToSignalRAsync(accessToken, new UpdateStatusRequest { Online = true });

                HttpContext.Session.SetString("ACCESS_TOKEN", accessToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Критическая ошибка при входе через внешнего провайдера");
                ErrorMessage = "Внутренняя ошибка при входе";
                return Page();
            }

            return RedirectToPage("/Account/Chats", new { tokenSaved = true });
        }

        public async Task<HttpResponseMessage> LoginAsync(CreateLoginRequest request, CancellationToken token = default)
        {
            return await _api.PostRawAsync("logins", request, request.Token, token);
        }

        private async Task SendToSignalRAsync(string? accessToken, UpdateStatusRequest request)
        {
            var statusResponse = await _api.PutRawAsync("userstatuses", request, accessToken);

            if (!statusResponse.IsSuccessStatusCode)
                return;

            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? User.FindFirst("sub")?.Value;

            if (!Guid.TryParse(userIdStr, out Guid userId))
                return;

            try
            {
                var payload = new
                {
                    userId = userId.ToString(),
                    isOnline = true,
                    lastActivity = DateTime.UtcNow
                };

                await _hubContext.Clients.All.SendAsync("UserOnlineStatusChanged", payload);
                await _hubContext.Clients.User(userId.ToString())
                    .SendAsync("UserOnlineStatusChanged", payload);

                _logger.LogInformation("SignalR уведомление о входе отправлено для пользователя {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Не удалось отправить SignalR уведомление о входе: {Message}", ex.Message);
            }
        }

        private static string GetLocalIPv4()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        return addr.Address.ToString();
                }
            }

            return "IPv4 адрес компьютера не найден";
        }
    }
}