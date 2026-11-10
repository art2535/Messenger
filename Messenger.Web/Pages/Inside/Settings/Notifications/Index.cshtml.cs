using Messenger.Web.Helpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace Messenger.Web.Pages.Inside.Settings.Notifications
{
    [Authorize]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class IndexModel : PageModel
    {
        private readonly ApiHelper _api;
        public IndexModel(ApiHelper api)
        {
            _api = api;
        }

        public string DisplayName { get; private set; } = "";
        public string AccessToken { get; private set; } = "";
        public string ApiBaseUrl { get; private set; } = "";

        public async Task OnGetAsync()
        {
            await InitializeAsync();
        }

        private async Task InitializeAsync()
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

            ApiBaseUrl = _api.GetApiUrl();

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
        }
    }
}
