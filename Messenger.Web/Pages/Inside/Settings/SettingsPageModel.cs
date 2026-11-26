using Messenger.Web.Helpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace Messenger.Web.Pages.Inside.Settings
{
    /// <summary>
    /// Базовая модель страниц настроек: токен, DisplayName, ApiBaseUrl → ViewData для layout.
    /// </summary>
    [Authorize]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public abstract class SettingsPageModel : PageModel
    {
        protected readonly ApiHelper Api;

        protected SettingsPageModel(ApiHelper api) { Api = api; }

        public string DisplayName { get; protected set; } = "";
        public string AccessToken { get; protected set; } = "";
        public string ApiBaseUrl { get; protected set; } = "";
        public string HubUrl { get; protected set; } = "";

        protected async Task InitializeSettingsAsync()
        {
            AccessToken = await HttpContext.GetTokenAsync("access_token") ?? "";

            if (string.IsNullOrEmpty(AccessToken))
            {
                var authResult = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                if (authResult.Succeeded)
                    AccessToken = authResult.Properties.GetTokenValue("access_token") ?? "";
            }

            if (string.IsNullOrEmpty(AccessToken))
                AccessToken = HttpContext.Session.GetString("ACCESS_TOKEN") ?? "";

            if (!string.IsNullOrEmpty(AccessToken))
                HttpContext.Session.SetString("ACCESS_TOKEN", AccessToken);

            ApiBaseUrl = Api.GetApiUrl();
            HubUrl = $"{ApiBaseUrl.TrimEnd('/')}/hubs/chat";

            var ln = User.FindFirst(ClaimTypes.Surname)?.Value
                     ?? User.FindFirst("family_name")?.Value ?? "";
            var fn = User.FindFirst(ClaimTypes.GivenName)?.Value
                     ?? User.FindFirst("given_name")?.Value ?? "";
            DisplayName = (ln + " " + fn).Trim();
            if (string.IsNullOrEmpty(DisplayName))
            {
                DisplayName = User.FindFirst("name")?.Value
                              ?? User.Identity?.Name
                              ?? User.FindFirst("preferred_username")?.Value
                              ?? "";
            }

            ViewData["DisplayName"] = DisplayName;
            ViewData["AccessToken"] = AccessToken;
            ViewData["ApiBaseUrl"] = ApiBaseUrl;
            ViewData["HubUrl"] = HubUrl;
            if (ViewData["Title"] == null)
                ViewData["Title"] = "Настройки";
        }
    }
}
