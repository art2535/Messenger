using Messenger.Core.DTOs.Users;
using Messenger.Web.Helpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Messenger.Web.Pages.Account
{
    [Authorize]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class SettingsModel : PageModel
    {
        private readonly ApiHelper _api;

        public SettingsModel(ApiHelper api)
        {
            _api = api;
        }

        public string ApiBaseUrl { get; private set; } = string.Empty;
        public string HubUrl { get; private set; } = string.Empty;

        [BindProperty]
        public UpdateUserProfileRequest Profile { get; set; } = new();

        [BindProperty]
        public IFormFile? AvatarFile { get; set; }

        [BindProperty]
        public bool DeleteAvatar { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool TokenSaved { get; set; }

        public bool HasAvatar { get; private set; }
        public string? AvatarUrl { get; private set; }
        public UserProfileDto? CurrentUser { get; private set; }
        public List<BlockedUserDto> BlockedUsers { get; private set; } = new();

        public string? ErrorMessage { get; private set; }
        public string AccessToken { get; set; } = string.Empty;

        public async Task<IActionResult> OnGetAsync()
        {
            if (Request.Query.ContainsKey("handler") || Request.Query.ContainsKey("refreshed"))
                return RedirectToPage("/Account/Settings", new { TokenSaved = true });

            if (User.Identity?.IsAuthenticated != true && !TokenSaved)
                return Redirect("/Authorization/Authorization");

            await InitializeAsync();
            await LoadProfileAsync();
            return Page();
        }

        public async Task<IActionResult> OnGetTokenAsync()
        {
            await InitializeAsync();
            Response.Headers.CacheControl = "no-store";
            return new JsonResult(new { accessToken = AccessToken });
        }

        private async Task LoadProfileAsync()
        {
            try
            {
                var profileRes = await _api.GetRawAsync("users/info", AccessToken);

                if (!profileRes.IsSuccessStatusCode)
                {
                    ErrorMessage = $"Ошибка API /users/info: {profileRes.StatusCode}";
                    return;
                }

                var json = await profileRes.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                var response = JsonSerializer.Deserialize<ApiResponse<UserProfileDto>>(json, options);

                if (response?.IsSuccess == true && response.Data != null)
                {
                    CurrentUser = response.Data;

                    Profile = new UpdateUserProfileRequest
                    {
                        LastName = CurrentUser.LastName ?? "",
                        FirstName = CurrentUser.FirstName ?? "",
                        Login = CurrentUser.Login ?? "",
                        Theme = CurrentUser.Account?.Theme ?? "light"
                    };

                    AvatarUrl = CurrentUser.Account?.Avatar;
                    HasAvatar = !string.IsNullOrWhiteSpace(AvatarUrl);
                    if (HasAvatar)
                        AvatarUrl += "?t=" + DateTimeOffset.Now.ToUnixTimeSeconds();
                }
                else
                {
                    ErrorMessage = "Профиль не найден в ответе API (isSuccess или data отсутствует)";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Ошибка при загрузке настроек: " + ex.Message;
            }
        }

        public async Task<IActionResult> OnPostSaveAsync()
        {
            await InitializeAsync();

            if (!ModelState.IsValid)
            {
                await LoadProfileAsync();
                return Page();
            }

            try
            {
                string? newAvatarUrl = null;

                if (DeleteAvatar)
                {
                    var deleteRes = await _api.DeleteRawAsync("users/delete-avatar", AccessToken);
                    if (!deleteRes.IsSuccessStatusCode)
                        ModelState.AddModelError("", "Не удалось удалить аватар");
                }
                else if (AvatarFile != null && AvatarFile.Length > 0)
                {
                    if (AvatarFile.Length > 2 * 1024 * 1024)
                    {
                        ModelState.AddModelError("", "Файл не должен превышать 2 МБ");
                        await LoadProfileAsync();
                        return Page();
                    }

                    using var content = new MultipartFormDataContent();
                    var fileContent = new StreamContent(AvatarFile.OpenReadStream());
                    fileContent.Headers.ContentType =
                        new MediaTypeHeaderValue(AvatarFile.ContentType ?? "image/jpeg");
                    content.Add(fileContent, "avatarFile", AvatarFile.FileName);

                    var uploadRes = await _api.PostMultipartRawAsync(
                        "users/upload-avatar", content, AccessToken);

                    if (uploadRes.IsSuccessStatusCode)
                    {
                        var json = await uploadRes.Content.ReadAsStringAsync();
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var result = JsonSerializer.Deserialize<ApiResponse<AvatarUploadResponse>>(json, options);
                        newAvatarUrl = result?.Data?.AvatarUrl;
                    }
                    else
                    {
                        ModelState.AddModelError("", "Ошибка загрузки аватара");
                    }
                }

                var payload = new
                {
                    Profile.LastName,
                    Profile.FirstName,
                    Profile.Login,
                    Profile.Theme
                };

                var updatePath = "users/update-profile";
                if (!string.IsNullOrEmpty(newAvatarUrl))
                    updatePath += $"?avatarUrl={Uri.EscapeDataString(newAvatarUrl)}";

                var response = await _api.PutRawAsync(updatePath, payload, AccessToken);

                if (response.IsSuccessStatusCode)
                {
                    TempData["SuccessMessage"] = "Настройки успешно сохранены";
                    return RedirectToPage("/Account/Settings", new { TokenSaved = true });
                }

                ModelState.AddModelError("", $"Ошибка обновления профиля: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Ошибка сохранения: {ex.Message}");
            }

            await LoadProfileAsync();
            return Page();
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
            {
                AccessToken = HttpContext.Session.GetString("ACCESS_TOKEN") ?? "";
            }
            if (!string.IsNullOrEmpty(AccessToken))
            {
                HttpContext.Session.SetString("ACCESS_TOKEN", AccessToken);
            }

            ApiBaseUrl = _api.GetApiUrl();
            HubUrl = $"{ApiBaseUrl}/hubs/chat";
        }
    }
}