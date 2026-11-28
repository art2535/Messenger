using Messenger.Application.DTOs.Responses;
﻿using System.ComponentModel;
using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.API.Adapters;
using Messenger.Application.DTOs.Users;
using Messenger.Application.Interfaces;
using Messenger.Realtime.Hubs;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

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
        private readonly IAdminService _adminService;
        private readonly IChatService _chatService;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ILogger<UsersController> _logger;

        public UsersController(
            IUserService userService,
            IAdminService adminService,
            IChatService chatService,
            IHubContext<ChatHub> hubContext,
            ILogger<UsersController> logger)
        {
            _userService = userService;
            _adminService = adminService;
            _chatService = chatService;
            _hubContext = hubContext;
            _logger = logger;
        }

        private IActionResult ForbiddenResult(string message) =>
            StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Failure(message, "FORBIDDEN"));

        private async Task SendProfileToPeersAsync(Guid userId, string method, object payload, CancellationToken ct)
        {
            var peerIds = await _chatService.GetPeerUserIdsAsync(userId, ct);
            var groups = peerIds.Select(id => $"User_{id}").ToList();

            if (groups.Count == 0)
            {
                await _hubContext.Clients.Group($"User_{userId}").SendAsync(method, payload, ct);
                return;
            }

            await _hubContext.Clients.Groups(groups).SendAsync(method, payload, ct);
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
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetUserByExternalIdAsync(
            [Description("Внешний идентификатор пользователя из SSO")] string externalId,
            CancellationToken ct = default)
        {
            try
            {
                var currentExternalId = User.GetExternalId();
                if (!string.Equals(currentExternalId, externalId, StringComparison.Ordinal))
                {
                    var currentUser = await _userService.GetUserByExternalIdAsync(currentExternalId, ct);
                    if (currentUser == null ||
                        !await _adminService.IsSystemAdminAsync(currentUser.UserId, ct))
                    {
                        return ForbiddenResult("Недостаточно прав для просмотра другого пользователя.");
                    }
                }

                var user = await _userService.GetUserByExternalIdAsync(externalId, ct);

                return Ok(ApiResponse.Success(user));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
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

                var result = await _userService.SearchUsersAsync(query.Trim(), ct);

                var data = result.Select(u => new
                {
                    Id = u.Id,
                    Name = u.Name,
                    Avatar = u.Avatar ?? "https://static.photos/people/200x200/default"
                });

                return Ok(ApiResponse.Success(data));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
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
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAllUsersAsync(CancellationToken ct = default)
        {
            try
            {
                var currentUser = await User.GetCurrentUserAsync(_userService, ct);
                if (!await _adminService.IsSystemAdminAsync(currentUser.UserId, ct))
                    return ForbiddenResult("Требуются права администратора.");

                var users = await _userService.GetAllUsersAsync(ct);

                return Ok(ApiResponse.Success(users));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
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
                var roles = await _userService.GetRolesAsync(ct);

                return Ok(ApiResponse.Success(roles));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
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
                var currentUser = await _userService.GetUserByIdAsync(userId, ct);
                if (currentUser == null)
                {
                    return NotFound(ApiResponse.Failure("Пользователь не найден", "NOT_FOUND"));
                }

                var name = $"{currentUser.FirstName} {currentUser.LastName}".Trim() ?? "Удалённый пользователь";

                return Ok(ApiResponse.Success(data: name));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
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
                var user = await User.GetCurrentUserAsync(_userService, ct);

                var currentUser = await _userService.GetUserByIdAsync(user.UserId, ct);
                if (currentUser == null)
                {
                    return NotFound(ApiResponse.Failure("Пользователь не найден", "NOT_FOUND"));
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

                return Ok(ApiResponse.Success(response));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении информации о текущем пользователе");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
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
                var user = await User.GetCurrentUserAsync(_userService, ct);

                await _userService.UpdateProfileAsync(user.UserId, request, avatarUrl, ct);

                var updatedUser = await _userService.GetUserByIdAsync(user.UserId, ct);
                if (updatedUser == null)
                {
                    return NotFound(ApiResponse.Failure("Пользователь не найден", "NOT_FOUND"));
                }

                var newDisplayName = $"{updatedUser.FirstName} {updatedUser.LastName}".Trim();
                var currentAvatar = updatedUser.Account?.Avatar;

                await SendProfileToPeersAsync(user.UserId, "ProfileUpdated", new
                {
                    UserId = user.UserId.ToString(),
                    AvatarUrl = currentAvatar,
                    DisplayName = newDisplayName
                }, ct);

                return Ok(ApiResponse.Success(message: "Пользователь успешно обновлен"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при обновлении профиля");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
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

                var user = await User.GetCurrentUserAsync(_userService, ct);

                var avatarUrl = await _userService.UploadAvatarAsync(user.UserId, new FormFileUploadAdapter(avatarFile), ct);

                await SendProfileToPeersAsync(user.UserId, "AvatarUpdated", new
                {
                    UserId = user.UserId.ToString(),
                    AvatarUrl = avatarUrl
                }, ct);

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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
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
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostAssignRoleAsync(
            [Description("Идентификатор роли")] Guid roleId,
            CancellationToken ct = default)
        {
            try
            {
                var user = await User.GetCurrentUserAsync(_userService, ct);
                if (!await _adminService.IsSystemAdminAsync(user.UserId, ct))
                    return ForbiddenResult("Требуются права администратора.");

                await _userService.AssignRoleAsync(user.UserId, roleId, ct);

                return Ok(ApiResponse.Success(message: "Роль успешно назначена"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при назначении роли");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
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
                var user = await User.GetCurrentUserAsync(_userService, ct);

                await _userService.DeleteAvatarAsync(user.UserId, ct);

                await SendProfileToPeersAsync(user.UserId, "AvatarUpdated", new
                {
                    UserId = user.UserId.ToString(),
                    AvatarUrl = (string?)null
                }, ct);

                return Ok(ApiResponse.Success(message: "Аватар успешно удалён"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при удалении аватара");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении аватара");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }
    }
}