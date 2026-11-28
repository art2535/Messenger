using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.Application.DTOs.Responses;
using Messenger.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для получения уведомлений.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/notifications")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [Tags("Notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly INotificationService _notificationService;
        private readonly IUserService _userService;
        private readonly ILogger<NotificationsController> _logger;

        public NotificationsController(
            INotificationService notificationService,
            IUserService userService,
            ILogger<NotificationsController> logger)
        {
            _notificationService = notificationService;
            _userService = userService;
            _logger = logger;
        }

        /// <summary>
        /// Получить уведомления текущего пользователя.
        /// </summary>
        [HttpGet]
        [EndpointName("GetNotifications")]
        [EndpointSummary("Получить уведомления текущего пользователя")]
        [EndpointDescription("Возвращает список всех активных и непрочитанных уведомлений авторизованного пользователя.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetNotificationsAsync(CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);
                var notifications = await _notificationService.GetNotificationsAsync(userId, ct);

                return Ok(ApiResponse.Success(notifications));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении уведомлений");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /notifications");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }
    }
}