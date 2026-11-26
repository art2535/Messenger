using System.ComponentModel;
using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.Core.DTOs.Users;
 using Messenger.Core.Interfaces;
using Messenger.Infrastructure.Interfaces.Users;
 using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
 using Messenger.Core.Responses;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для управления пользователями.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/users")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [Tags("Users")]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IUserWorkflowService _userWorkflowService;
        private readonly ILogger<UsersController> _logger;

        public UsersController(
            IUserService userService,
            IUserWorkflowService userWorkflowService,
            ILogger<UsersController> logger)
        {
            _userService = userService;
            _userWorkflowService = userWorkflowService;
            _logger = logger;
        }

         /// <summary>
        /// Получить пользователя по внешнему идентификатору (OIDC / Keycloak).
        /// </summary>
        [HttpGet("{externalId}")]
        [EndpointName("GetUserByExternalId")]
        [EndpointSummary("Получить пользователя по externalId")]
        [EndpointDescription("Возвращает пользователя по внешнему идентификатору из SSO (Keycloak).")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetUserByExternalIdAsync(
            [Description("Внешний идентификатор пользователя из SSO")] string externalId,
            CancellationToken ct = default)
        {
            try
            {
                var user = await _userWorkflowService.GetByExternalIdAsync(externalId);

                return Ok(ApiResponse.Success(user));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении пользователя по externalId {ExternalId}", externalId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Поиск пользователей.
        /// </summary>
        [HttpGet("search")]
        [EndpointName("SearchUsers")]
        [EndpointSummary("Поиск пользователей")]
        [EndpointDescription("Возвращает список пользователей, соответствующих поисковому запросу (минимум 2 символа). Используется для автодополнения при добавлении в чат или поиске контактов.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetSearchAsync(
            [FromQuery, Description("Поисковый запрос (минимум 2 символа)")] string query,
            CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
                {
                    return Ok(ApiResponse.Success(Array.Empty<object>()
                    ));
                }

                var data = await _userWorkflowService.SearchAsync(query, ct);

                return Ok(ApiResponse.Success(data));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при поиске пользователей по запросу {Query}", query);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Получить список всех пользователей.
        /// </summary>
        [HttpGet]
        [EndpointName("GetAllUsers")]
        [EndpointSummary("Получить список всех пользователей")]
        [EndpointDescription("Возвращает полный список зарегистрированных пользователей системы.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAllUsersAsync(CancellationToken ct = default)
        {
            try
            {
                var users = await _userWorkflowService.GetAllAsync(ct);

                return Ok(ApiResponse.Success(users));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении списка всех пользователей");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Получить список всех ролей.
        /// </summary>
        [HttpGet("roles")]
        [EndpointName("GetAllRoles")]
        [EndpointSummary("Получить список всех ролей")]
        [EndpointDescription("Возвращает список всех доступных ролей в системе.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAllRolesAsync(CancellationToken ct = default)
        {
            try
            {
                var roles = await _userWorkflowService.GetRolesAsync(ct);

                return Ok(ApiResponse.Success(roles));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении списка всех ролей");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Получить отображаемое имя пользователя по ID.
        /// </summary>
        [HttpGet("{userId}/name")]
        [EndpointName("GetUserDisplayName")]
        [EndpointSummary("Получить отображаемое имя пользователя")]
        [EndpointDescription("Возвращает полное имя (Имя Фамилия) пользователя по его GUID.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetDisplayNameAsync(
            [Description("Идентификатор пользователя (GUID)")] Guid userId,
            CancellationToken ct = default)
        {
            try
            {
                var name = await _userWorkflowService.GetDisplayNameAsync(userId, ct);
                if (name == null)
                {
                    return NotFound(ApiResponse.Failure("Пользователь не найден", "NOT_FOUND"));
                }

                return Ok(ApiResponse.Success(data: name));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении отображаемого имени пользователя {UserId}", userId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Получить информацию о текущем пользователе.
        /// </summary>
        [HttpGet("info")]
        [EndpointName("GetCurrentUserInfo")]
        [EndpointSummary("Получить информацию о текущем пользователе")]
        [EndpointDescription("Возвращает личные данные авторизованного пользователя: имя, логин, телефон, аватар и тему.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetInfoAsync(CancellationToken ct = default)
        {
            try
            {
                var user = await User.GetCurrentUserAsync(_userService);

                var response = await _userWorkflowService.GetCurrentUserInfoAsync(user.UserId, ct);
                if (response == null)
                {
                    return NotFound(ApiResponse.Failure("Пользователь не найден", "NOT_FOUND"));
                }

                return Ok(ApiResponse.Success(response));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении информации о текущем пользователе");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении информации о текущем пользователе");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Обновить профиль текущего пользователя.
        /// </summary>
        [HttpPut("update-profile")]
        [EndpointName("UpdateUserProfile")]
        [EndpointSummary("Обновить профиль текущего пользователя")]
        [EndpointDescription("Обновляет личные данные пользователя (имя, телефон и т.д.) и тему интерфейса. Аватар можно передать отдельно через upload-avatar.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PutUpdateProfileAsync(
            [FromBody, Description("Новые данные профиля")] UpdateUserProfileRequest request,
            [FromQuery, Description("Опциональный URL аватара")] string? avatarUrl = null,
            CancellationToken ct = default)
        {
            try
            {
                var user = await User.GetCurrentUserAsync(_userService);

                await _userWorkflowService.UpdateProfileAsync(user, request, avatarUrl, ct);

                return Ok(ApiResponse.Success(message: "Пользователь успешно обновлен"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при обновлении профиля");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении профиля пользователя");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Загрузить аватар пользователя.
        /// </summary>
        [HttpPost("upload-avatar")]
        [Consumes("multipart/form-data")]
        [EndpointName("UploadAvatar")]
        [EndpointSummary("Загрузить аватар пользователя")]
        [EndpointDescription("Загружает изображение аватара для текущего пользователя (макс. 2 МБ). Возвращает URL загруженного аватара.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostUploadAvatarAsync(
            [FromForm, Description("Файл аватара (изображение)")] IFormFile avatarFile,
            CancellationToken ct = default)
        {
            try
            {
                if (avatarFile == null || avatarFile.Length == 0)
                {
                    return BadRequest(ApiResponse.Failure("Файл не выбран", "INVALID_REQUEST"));
                }

                if (avatarFile.Length > 2 * 1024 * 1024)
                {
                    return BadRequest(ApiResponse.Failure("Файл не должен превышать 2 МБ", "INVALID_REQUEST"));
                }

                var user = await User.GetCurrentUserAsync(_userService);

                var avatarUrl = await _userWorkflowService.UploadAvatarAsync(user, avatarFile, ct);

                return Ok(ApiResponse.Success(new { avatarUrl }, "Аватар успешно загружен"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при загрузке аватара");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при загрузке аватара");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Назначить роль текущему пользователю.
        /// </summary>
        [HttpPost("assign-role/{roleId}")]
        [EndpointName("AssignRole")]
        [EndpointSummary("Назначить роль текущему пользователю")]
        [EndpointDescription("Назначает указанную роль авторизованному пользователю (для администраторов).")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostAssignRoleAsync(
            [Description("Идентификатор роли")] Guid roleId,
            CancellationToken ct = default)
        {
            try
            {
                var user = await User.GetCurrentUserAsync(_userService);

                await _userWorkflowService.AssignRoleAsync(user.UserId, roleId, ct);

                return Ok(ApiResponse.Success(message: "Роль успешно назначена"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при назначении роли");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при назначении роли {RoleId} пользователю", roleId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Удалить аватар.
        /// </summary>
        [HttpDelete("delete-avatar")]
        [EndpointName("DeleteAvatar")]
        [EndpointSummary("Удалить аватар")]
        [EndpointDescription("Удаляет текущий аватар пользователя и устанавливает стандартный. Уведомление рассылается через SignalR.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteAvatarAsync(CancellationToken ct = default)
        {
            try
            {
                var user = await User.GetCurrentUserAsync(_userService);

                await _userWorkflowService.DeleteAvatarAsync(user, ct);

                return Ok(ApiResponse.Success(message: "Аватар успешно удалён"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при удалении аватара");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении аватара");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }
    }
}