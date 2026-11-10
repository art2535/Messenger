using Messenger.Core.DTOs.Auth;
using Messenger.Core.DTOs.Logins;
using Messenger.Core.DTOs.UserStatuses;
using Messenger.Core.Hubs;
using Messenger.Web.Helpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Claims;
using Messenger.Web.Services;

namespace Messenger.Web.Pages.Login
{
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [AllowAnonymous]
    public class IndexModel : PageModel
    {
        private readonly ApiHelper _api;
        private readonly ILogger<IndexModel> _logger;
        private readonly IHubContext<ChatHub> _hubContext;

        public string ErrorMessage { get; private set; } = string.Empty;

        [BindProperty(SupportsGet = true)]
        public bool LoggedOut { get; set; }

        public IndexModel(ApiHelper api, ILogger<IndexModel> logger, IHubContext<ChatHub> hubContext)
        {
            _api = api;
            _logger = logger;
            _hubContext = hubContext;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (LoggedOut)
            {
                if (User.Identity?.IsAuthenticated == true)
                {
                    try
                    {
                        await HttpContext.SignOutAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                    catch { }
                }
                return Page();
            }

            if (User.Identity?.IsAuthenticated == true)
            {
                var accessToken = await HttpContext.GetTokenAsync("access_token");

                if (!string.IsNullOrEmpty(accessToken))
                {
                    HttpContext.Session.SetString("ACCESS_TOKEN", accessToken);
                    var tokenCopy = accessToken;
                    var sessionKey = await EnsureSessionKeyAsync();
                    var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                 ?? User.FindFirst("sub")?.Value;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var loginRequest = new CreateLoginRequest
                            {
                                Token = sessionKey, // ключ UserTokenStore для отзыва refresh при revoke
                                IpAddress = GetLocalIPv4()
                            };
                            await LoginAsync(loginRequest);
                            await SendToSignalRAsync(tokenCopy, new UpdateStatusRequest { Online = true }, userIdStr);
                        }
                        catch (Exception bgEx)
                        {
                            _logger.LogWarning(bgEx, "Фоновая запись входа при уже аутентифицированном OnGet");
                        }
                    });
                }

                return RedirectToPage("/Inside/Chats/Index");
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
                    HttpContext.Session.SetString("ACCESS_TOKEN", accessToken!);
                    var tokenCopy = accessToken;
                    var sessionKey = await EnsureSessionKeyAsync();
                    var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                 ?? User.FindFirst("sub")?.Value;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await LoginAsync(new CreateLoginRequest { Token = sessionKey, IpAddress = GetLocalIPv4() });
                            await SendToSignalRAsync(tokenCopy, new UpdateStatusRequest { Online = true }, userIdStr);
                        }
                        catch (Exception bgEx)
                        {
                            _logger.LogWarning(bgEx, "Фоновая запись входа (EtaLogin)");
                        }
                    });
                }
                return RedirectToPage("/Inside/Chats/Index");
            }

            var properties = new AuthenticationProperties
            {
                RedirectUri = Url.Page("/Login/Index", "Callback")
            };

            return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
        }

        public async Task<IActionResult> OnGetCallbackAsync()
        {
            var externalId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(externalId))
            {
                ErrorMessage = "Нет externalId";
                return RedirectToPage("/Login/Index", new { error = "Нет externalId" });
            }

            var firstName = User.FindFirst(ClaimTypes.GivenName)?.Value ?? "ЕТА";
            var lastName = User.FindFirst(ClaimTypes.Surname)?.Value ?? "Пользователь";
            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var roles = ExtractRolesFromClaims(User);

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
                    Roles = roles
                };

                var authResponse = await _api.PostRawAsync("authorization/external/callback", request, accessToken);

                if (!authResponse.IsSuccessStatusCode)
                {
                    var error = await authResponse.Content.ReadAsStringAsync();
                    _logger.LogError("Ошибка внешней авторизации: {StatusCode} - {Error}", authResponse.StatusCode, error);
                    ErrorMessage = "Ошибка авторизации в системе";
                    return Page();
                }

                HttpContext.Session.SetString("ACCESS_TOKEN", accessToken);

                var tokenCopy = accessToken;
                var sessionKey = await EnsureSessionKeyAsync();
                var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? User.FindFirst("sub")?.Value;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var loginRequest = new CreateLoginRequest
                        {
                            Token = sessionKey,
                            IpAddress = GetLocalIPv4()
                        };
                        await LoginAsync(loginRequest);
                        await SendToSignalRAsync(tokenCopy, new UpdateStatusRequest { Online = true }, userIdStr);
                    }
                    catch (Exception bgEx)
                    {
                        _logger.LogWarning(bgEx, "Фоновая запись входа/статуса после OIDC");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Критическая ошибка при входе через внешнего провайдера");
                ErrorMessage = "Внутренняя ошибка при входе";
                return Page();
            }

            return RedirectToPage("/Inside/Chats/Index");
        }

        private List<string> ExtractRolesFromClaims(ClaimsPrincipal user)
        {
            var roles = new List<string>();

            roles.AddRange(user.FindAll(ClaimTypes.Role).Select(c => c.Value));
            roles.AddRange(user.FindAll("role").Select(c => c.Value));

            var realmAccessClaim = user.FindFirst("realm_access")?.Value;
            if (!string.IsNullOrEmpty(realmAccessClaim))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(realmAccessClaim);
                    if (doc.RootElement.TryGetProperty("roles", out var rolesElement) &&
                        rolesElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var role in rolesElement.EnumerateArray())
                        {
                            var roleValue = role.GetString();
                            if (!string.IsNullOrEmpty(roleValue))
                                roles.Add(roleValue);
                        }
                    }
                }
                catch
                {
                    _logger.LogError("Ошибка получения роли");
                }
            }

            return roles
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Where(r => r.StartsWith("ROLE_", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<HttpResponseMessage> LoginAsync(CreateLoginRequest request, CancellationToken token = default)
        {
            return await _api.PostRawAsync("logins", request, request.Token, token);
        }

        private async Task SendToSignalRAsync(string? accessToken, UpdateStatusRequest request, string? userIdStr = null)
        {
            var statusResponse = await _api.PutRawAsync("userstatuses", request, accessToken);

            if (!statusResponse.IsSuccessStatusCode)
                return;

            if (string.IsNullOrEmpty(userIdStr))
                return;

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

                await _hubContext.Clients.Group($"User_{userId}").SendAsync("UserOnlineStatusChanged", payload);

                _logger.LogInformation("SignalR уведомление о входе отправлено для пользователя {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Не удалось отправить SignalR уведомление о входе: {Message}", ex.Message);
            }
        }

        private async Task<string> EnsureSessionKeyAsync()
        {
            var auth = await HttpContext.AuthenticateAsync(
                Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
            var props = auth.Properties ?? new AuthenticationProperties();
            if (!props.Items.TryGetValue(UserTokenStore.StoreKeyItem, out var key) || string.IsNullOrEmpty(key))
            {
                key = Guid.NewGuid().ToString("N");
                props.Items[UserTokenStore.StoreKeyItem] = key;
                if (auth.Principal != null)
                {
                    await HttpContext.SignInAsync(
                        Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
                        auth.Principal, props);
                }
            }

            var access = await HttpContext.GetTokenAsync("access_token") ?? "";
            var refresh = await HttpContext.GetTokenAsync("refresh_token") ?? "";
            if (!string.IsNullOrEmpty(access) && !string.IsNullOrEmpty(refresh))
            {
                var store = HttpContext.RequestServices.GetRequiredService<UserTokenStore>();
                store.GetOrAdd(key!, access, refresh).LastSeenUtc = DateTime.UtcNow;
            }
            return key!;
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