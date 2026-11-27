using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.Application.DTOs.UserStatuses;
using Messenger.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;

using Messenger.Application.DTOs.Responses;
namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для управления статусами пользователей.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/user-statuses")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [Tags("UserStatuses")]
    public class UserStatusesController : ControllerBase
    {
        private readonly IUserStatusService _userStatusService;
        private readonly IUserService _userService;
        private readonly ILogger<UserStatusesController> _logger;

        public UserStatusesController(
            IUserStatusService userStatusService,
            IUserService userService,
            ILogger<UserStatusesController> logger)
        {
            _userStatusService = userStatusService;
            _userService = userService;
            _logger = logger;
        }

        /// <summary>
        /// Получить статус текущего пользователя.
        /// </summary>
        [HttpGet]
        [EndpointName("GetUserStatus")]
        [EndpointSummary("Получить статус текущего пользователя")]
        [EndpointDescription("Возвращает текущий статус авторизованного пользователя: онлайн/офлайн и время последней активности.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetUserStatusAsync(CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);

                var status = await _userStatusService.GetUserStatusByUserIdAsync(userId, ct);

                if (status == null)
                {
                    return NotFound(ApiResponse.Failure("Статус пользователя не найден", "NOT_FOUND"));
                }

                return Ok(ApiResponse.Success(status));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении статуса пользователя");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении статуса пользователя");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Обновить статус текущего пользователя.
        /// </summary>
        [HttpPut]
        [EndpointName("UpdateUserStatus")]
        [EndpointSummary("Обновить статус текущего пользователя")]
        [EndpointDescription("Обновляет статус онлайн/офлайн для авторизованного пользователя. Поле LastActivity автоматически устанавливается на текущее время сервера.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PutUpdateStatusAsync(
            [FromBody, Description("Новый статус пользователя")] UpdateUserStatusDto request,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);

                await _userStatusService.UpdateUserStatusAsync(userId, request.Online, ct);

                return Ok(ApiResponse.Success(message: "Статус пользователя обновлен"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при обновлении статуса пользователя");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении статуса пользователя");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }
    }
}
