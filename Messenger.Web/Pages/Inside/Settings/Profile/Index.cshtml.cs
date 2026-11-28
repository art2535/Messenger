using Messenger.Application.DTOs.Responses;
using Messenger.Application.DTOs.Users;
using Messenger.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Messenger.Web.Pages.Inside.Settings.Profile
{
    [Authorize]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class IndexModel : SettingsPageModel
    {
        public IndexModel(ApiHelper api) : base(api) { }

        [BindProperty]
        public UpdateUserProfileRequest Profile { get; set; } = new();

        [BindProperty]
        public IFormFile? AvatarFile { get; set; }

        [BindProperty]
        public bool DeleteAvatar { get; set; }

        public bool HasAvatar { get; private set; }
        public string? AvatarUrl { get; private set; }
        public UserProfileDto? CurrentUser { get; private set; }

        public string? ErrorMessage { get; private set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (Request.Query.ContainsKey("handler") || Request.Query.ContainsKey("refreshed"))
                return RedirectToPage("/Inside/Settings/Profile/Index");

            if (User.Identity?.IsAuthenticated != true)
                return Redirect("/login");

            await InitializeSettingsAsync();
            await LoadProfileAsync();
            return Page();
        }

        public async Task<IActionResult> OnGetTokenAsync()
        {
            await InitializeSettingsAsync();
            Response.Headers.CacheControl = "no-store";
            return new JsonResult(new { accessToken = AccessToken });
        }

        private async Task LoadProfileAsync()
        {
            try
            {
                var profileRes = await Api.GetRawAsync("users/info", AccessToken);

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
                    if (!string.IsNullOrWhiteSpace(AvatarUrl) && AvatarUrl.StartsWith("/"))
                    {
                        var apiOrigin = (Api.GetApiUrl() ?? "").Split(new[] { "/api/" }, StringSplitOptions.None)[0].TrimEnd('/');
                        AvatarUrl = apiOrigin + AvatarUrl;
                    }
                    HasAvatar = !string.IsNullOrWhiteSpace(AvatarUrl);
                    // cache-bust только в разметке (один ?t=), иначе URL ломается
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
            await InitializeSettingsAsync();

            if (!ModelState.IsValid)
            {
                await LoadProfileAsync();
                return Page();
            }

            try
            {
                if (DeleteAvatar)
                {
                    var deleteRes = await Api.DeleteRawAsync("users/delete-avatar", AccessToken);
                    if (!deleteRes.IsSuccessStatusCode)
                    {
                        ModelState.AddModelError("", "Не удалось удалить аватар");
                        await LoadProfileAsync();
                        return Page();
                    }
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

                    var uploadRes = await Api.PostMultipartRawAsync(
                        "users/upload-avatar", content, AccessToken);

                    if (!uploadRes.IsSuccessStatusCode)
                    {
                        ModelState.AddModelError("", "Ошибка загрузки аватара");
                        await LoadProfileAsync();
                        return Page();
                    }

                }

                var payload = new
                {
                    Profile.LastName,
                    Profile.FirstName,
                    Profile.Login,
                    Profile.Theme
                };

                var response = await Api.PutRawAsync("users/update-profile", payload, AccessToken);

                if (response.IsSuccessStatusCode)
                {
                    TempData["SuccessMessage"] = "Настройки успешно сохранены";
                    return RedirectToPage("/Inside/Settings/Profile/Index");
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
}
}
