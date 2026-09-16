using Asp.Versioning;
using Messenger.API.Responses;
using Messenger.API.Services;
using Messenger.Core.DTOs.Users;
using Messenger.Core.Hubs;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.ComponentModel;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для управления пользователями
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Tags("Users")]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IHubContext<ChatHub> _hubContext;

        public UsersController(IUserService userService, IHubContext<ChatHub> hubContext)
        {
            _userService = userService;
            _hubContext = hubContext;
        }

        /// <summary>
        /// Получить пользователя по внешнему идентификатору (OIDC / Keycloak)
        /// </summary>
        [HttpGet("{externalId}")]
        [EndpointName("GetUserByExternalId")]
        [EndpointSummary("Получить пользователя по externalId")]
        [EndpointDescription("Возвращает пользователя по внешнему идентификатору из SSO (Keycloak).")]
        [ProducesResponseType(typeof(User), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetUserByExternalIdAsync(
            [Description("Внешний идентификатор пользователя из SSO")] string externalId)
        {
            try
            {
                var user = await _userService.GetUserByExternalIdAsync(externalId);
                return Ok(user);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Поиск пользователей
        /// </summary>
        [HttpGet("search")]
        [EndpointName("SearchUsers")]
        [EndpointSummary("Поиск пользователей")]
        [EndpointDescription("Возвращает список пользователей, соответствующих поисковому запросу (минимум 2 символа). " +
            "Используется для автодополнения при добавлении в чат или поиске контактов.")]
        [ProducesResponseType(typeof(SearchUsersSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SearchAsync(
            [FromQuery, Description("Поисковый запрос (минимум 2 символа)")] string query, 
            CancellationToken token = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
                {
                    return Ok(new SearchUsersSuccessResponse
                    {
                        IsSuccess = true,
                        Data = Array.Empty<object>()
                    });
                }

                var result = await _userService.SearchUsersAsync(query.Trim(), token);

                var data = result.Select(u => new
                {
                    id = u.Id,
                    name = u.Name,
                    avatar = u.Avatar ?? "https://static.photos/people/200x200/default"
                });

                return Ok(new SearchUsersSuccessResponse
                {
                    IsSuccess = true,
                    Data = data
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Получить список всех пользователей
        /// </summary>
        [HttpGet]
        [EndpointName("GetAllUsers")]
        [EndpointSummary("Получить список всех пользователей")]
        [EndpointDescription("Возвращает полный список зарегистрированных пользователей системы.")]
        [ProducesResponseType(typeof(GetAllUsersSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAllUsersAsync(CancellationToken token = default)
        {
            try
            {
                var users = await _userService.GetAllUsersAsync(token);

                return Ok(new GetAllUsersSuccessResponse
                {
                    IsSuccess = true,
                    Data = users
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message,
                });
            }
        }

        /// <summary>
        /// Получить список всех ролей
        /// </summary>
        [HttpGet("roles")]
        [EndpointName("GetAllRoles")]
        [EndpointSummary("Получить список всех ролей")]
        [EndpointDescription("Возвращает список всех доступных ролей в системе.")]
        [ProducesResponseType(typeof(GetRolesSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAllRolesAsync(CancellationToken token = default)
        {
            try
            {
                var roles = await _userService.GetRolesAsync(token);

                return Ok(new GetRolesSuccessResponse
                {
                    IsSuccess = true,
                    Data = roles
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message,
                });
            }
        }

        /// <summary>
        /// Получить отображаемое имя пользователя по ID
        /// </summary>
        [HttpGet("{userId}/name")]
        [EndpointName("GetUserDisplayName")]
        [EndpointSummary("Получить отображаемое имя пользователя")]
        [EndpointDescription("Возвращает полное имя (Имя Фамилия) пользователя по его GUID.")]
        [ProducesResponseType(typeof(GetUserNameSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetUserDisplayName([Description("Идентификатор пользователя (GUID)")] Guid userId)
        {
            try
            {
                var currentUser = await _userService.GetUserByIdAsync(userId);
                if (currentUser == null)
                {
                    return NotFound(new ErrorResponse
                    {
                        IsSuccess = false,
                        Error = "Пользователь не найден"
                    });
                }

                var name = $"{currentUser.FirstName} {currentUser.LastName}".Trim() ?? "Удалённый пользователь";

                return Ok(new GetUserNameSuccessResponse 
                { 
                    IsSuccess = true, 
                    Data = name 
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Получить информацию о текущем пользователе
        /// </summary>
        [HttpGet("info")]
        [EndpointName("GetCurrentUserInfo")]
        [EndpointSummary("Получить информацию о текущем пользователе")]
        [EndpointDescription("Возвращает личные данные авторизованного пользователя: имя, логин, телефон, аватар и тему.")]
        [ProducesResponseType(typeof(GetCurrentUserSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetUserByIdAsync(CancellationToken token = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                {
                    return error;
                }

                var currentUser = await _userService.GetUserByIdAsync(user!.UserId, token);
                if (currentUser == null)
                {
                    return NotFound(new ErrorResponse
                    {
                        IsSuccess = false,
                        Error = "Пользователь не найден"
                    });
                }

                var response = new
                {
                    UserId = currentUser.UserId,
                    LastName = currentUser.LastName ?? "",
                    FirstName = currentUser.FirstName ?? "",
                    Login = currentUser.Login ?? "",
                    Account = currentUser.Account != null ? new
                    {
                        Avatar = currentUser.Account.Avatar,
                    } : null
                };

                return Ok(new GetCurrentUserSuccessResponse
                {
                    IsSuccess = true,
                    Data = response
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Обновить профиль текущего пользователя
        /// </summary>
        [HttpPut("update-profile")]
        [EndpointName("UpdateUserProfile")]
        [EndpointSummary("Обновить профиль текущего пользователя")]
        [EndpointDescription("Обновляет личные данные пользователя (имя, телефон и т.д.) и тему интерфейса. " +
            "Аватар можно передать отдельно через upload-avatar.")]
        [ProducesResponseType(typeof(UpdateProfileSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateUserProfileByIdAsync(
            [FromBody, Description("Новые данные профиля")] UpdateUserProfileRequest request,
            [FromQuery, Description("Опциональный URL аватара")] string? avatarUrl = null,
            CancellationToken token = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                {
                    return error;
                }

                await _userService.UpdateProfileAsync(user!.UserId, request, avatarUrl, token);

                var updatedUser = await _userService.GetUserByIdAsync(user!.UserId, token);
                var newDisplayName = $"{updatedUser.FirstName} {updatedUser.LastName}".Trim();
                var currentAvatar = updatedUser.Account?.Avatar;

                await _hubContext.Clients.All.SendAsync("ProfileUpdated", new
                {
                    userId = user!.UserId.ToString(),
                    avatarUrl = currentAvatar,
                    displayName = newDisplayName
                }, token);

                return Ok(new UpdateProfileSuccessResponse
                {
                    IsSuccess = true,
                    Message = "Пользователь успешно обновлен"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Загрузить аватар пользователя
        /// </summary>
        [HttpPost("upload-avatar")]
        [EndpointName("UploadAvatar")]
        [EndpointSummary("Загрузить аватар пользователя")]
        [EndpointDescription("Загружает изображение аватара для текущего пользователя (макс. 2 МБ). Возвращает URL загруженного аватара.")]
        [ProducesResponseType(typeof(UploadAvatarSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UploadAvatar(
            [FromForm, Description("Файл аватара (изображение)")] IFormFile avatarFile, 
            CancellationToken token = default)
        {
            if (avatarFile == null || avatarFile.Length == 0)
            {
                return BadRequest(new ErrorResponse
                {
                    IsSuccess = false,
                    Error = "Файл не выбран"
                });
            }

            if (avatarFile.Length > 2 * 1024 * 1024)
            {
                return BadRequest(new ErrorResponse
                {
                    IsSuccess = false,
                    Error = "Файл не должен превышать 2 МБ"
                });
            }

            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                {
                    return error;
                }

                var avatarUrl = await _userService.UploadAvatarAsync(user!.UserId, avatarFile, token);

                await _hubContext.Clients.All.SendAsync("AvatarUpdated", new
                {
                    userId = user!.UserId.ToString(),
                    avatarUrl = avatarUrl
                }, token);

                return Ok(new UploadAvatarSuccessResponse
                {
                    IsSuccess = true,
                    Message = "Аватар успешно загружен",
                    Data = new { avatarUrl }
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ErrorResponse 
                { 
                    IsSuccess = false, 
                    Error = ex.Message 
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse 
                { 
                    IsSuccess = false, 
                    Error = ex.Message 
                });
            }
        }

        /// <summary>
        /// Назначить роль текущему пользователю
        /// </summary>
        [HttpPost("assign-role/{roleId}")]
        [EndpointName("AssignRole")]
        [EndpointSummary("Назначить роль текущему пользователю")]
        [EndpointDescription("Назначает указанную роль авторизованному пользователю (для администраторов).")]
        [ProducesResponseType(typeof(AssignRoleSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AssignRoleAsync(
            [Description("Идентификатор роли")] Guid roleId, CancellationToken token = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                {
                    return error;
                }

                await _userService.AssignRoleAsync(user!.UserId, roleId, token);

                return Ok(new AssignRoleSuccessResponse
                {
                    IsSuccess = true,
                    Message = "Роль успешно определена"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Удалить аватар
        /// </summary>
        [HttpDelete("delete-avatar")]
        [EndpointName("DeleteAvatar")]
        [EndpointSummary("Удалить аватар")]
        [EndpointDescription("Удаляет текущий аватар пользователя и устанавливает стандартный. Уведомление рассылается через SignalR.")]
        [ProducesResponseType(typeof(DeleteAvatarSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteAvatar(CancellationToken token = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                {
                    return error;
                }

                await _userService.DeleteAvatarAsync(user!.UserId, token);

                await _hubContext.Clients.All.SendAsync("AvatarUpdated", new
                {
                    userId = user!.UserId.ToString(),
                    avatarUrl = (string?)null
                });

                return Ok(new DeleteAvatarSuccessResponse
                {
                    IsSuccess = true,
                    Message = "Аватар успешно удалён"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse 
                { 
                    IsSuccess = false, 
                    Error = ex.Message 
                });
            }
        }
    }
}
