using Messenger.Core.Models;
using Messenger.Web.Helpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace Messenger.Web.Pages.Account
{
    [Authorize]
    public class ChatsModel : PageModel
    {
        private readonly ApiHelper _api;
        private static readonly Dictionary<string, string> RoleDisplayNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ROLE_ENTRANT"] = "Абитуриент",
            ["ROLE_STUDENT"] = "Студент",
            ["ROLE_TEACHER"] = "Преподаватель",
            ["ROLE_EMPLOYEE"] = "Сотрудник",
            ["ROLE_ADMIN"] = "Администратор",
            ["ROLE_USER"] = "Пользователь"
        };
        private static readonly string[] RolePriority =
        [
            "ROLE_ADMIN",
            "ROLE_TEACHER",
            "ROLE_EMPLOYEE",
            "ROLE_STUDENT",
            "ROLE_ENTRANT",
            "ROLE_USER"
        ];

        public string? UserId { get; set; }
        public string? UserName { get; set; } = string.Empty;
        public string? UserRole { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool TokenSaved { get; set; }

        public string ApiBaseUrl { get; private set; } = string.Empty;
        public string HubUrl { get; private set; } = string.Empty;

        public ChatsModel(ApiHelper api)
        {
            _api = api;
        }

        private static string GetDisplayRole(ClaimsPrincipal user)
        {
            var userRoles = user.FindAll(ClaimTypes.Role)
                .Select(c => c.Value)
                .Where(r => r.StartsWith("ROLE_", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var role in RolePriority)
            {
                if (userRoles.Contains(role) && RoleDisplayNames.TryGetValue(role, out var displayName))
                    return displayName;
            }

            var firstRole = userRoles.FirstOrDefault();
            return firstRole != null && RoleDisplayNames.TryGetValue(firstRole, out var name)
                ? name
                : "Пользователь";
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (User.Identity?.IsAuthenticated != true && !TokenSaved)
                return RedirectToPage("/Authorization/Authorization");

            if (User.Identity?.IsAuthenticated == true)
            {
                var accessToken = await HttpContext.GetTokenAsync("access_token");

                var externalId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                var user = await _api.GetAsync<User>($"users/{Uri.EscapeDataString(externalId ?? "")}", accessToken);

                UserId = user?.UserId.ToString() ?? externalId;

                UserName = User.FindFirstValue("name") ?? User.FindFirstValue("preferred_username");

                UserRole = GetDisplayRole(User);

                if (!string.IsNullOrEmpty(accessToken))
                    HttpContext.Session.SetString("ACCESS_TOKEN", accessToken);
            }

            HttpContext.Session.SetString("USER_ID", UserId ?? "");
            HttpContext.Session.SetString("USER_NAME", UserName ?? "");
            HttpContext.Session.SetString("USER_ROLE", UserRole ?? "");

            ApiBaseUrl = _api.GetApiUrl();
            HubUrl = $"{ApiBaseUrl}/hubs/chat";

            return Page();
        }
    }
}