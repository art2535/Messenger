using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.Core.DTOs.Notifications;
using Messenger.Core.Interfaces;
using Messenger.Core.Responses;
using Messenger.Infrastructure.Interfaces.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для управления уведомлениями.
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
        private readonly INotificationWorkflowService _notificationWorkflowService;
        private readonly IUserService _userService;
        private readonly ILogger<NotificationsController> _logger;

        public NotificationsController(
            INotificationWorkflowService notificationWorkflowService,
            IUserService userService,
            ILogger<NotificationsController> logger)
        {
            _notificationWorkflowService = notificationWorkflowService;
            _userService = userService;
            _logger = logger;
        }

        /// <summary>
        /// Создать новое уведомление.
        /// </summary>
        [HttpPost]
        [EndpointName("CreateNotification")]
        [EndpointSummary("Создать новое уведомление")]
        [EndpointDescription(
            "Создаёт уведомление для указанного пользователя. Обычно вызывается " +
            "внутренними сервисами (например, при новом сообщении, добавлении в чат и т.д.).")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostCreateNotificationAsync(
            [FromBody, Description("Данные для создания уведомления")]
            CreateNotificationRequest request,
            CancellationToken ct = default)
        {
            try
            {
                await User.GetCurrentUserIdAsync(_userService);

                await _notificationWorkflowService.CreateNotificationAsync(
                    request.UserId,
                    request.Text,
                    ct);

                return Ok(ApiResponse.Success(message: "Уведомление успешно создано"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при создании уведомления");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (KeyNotFoundException)
            {
                return NotFound(ApiResponse.Failure(
                    $"Пользователь с ID {request.UserId} не найден",
                    "NOT_FOUND"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении POST /notifications");
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Получить уведомления текущего пользователя.
        /// </summary>
        [HttpGet]
        [EndpointName("GetNotifications")]
        [EndpointSummary("Получить уведомления текущего пользователя")]
        [EndpointDescription(
            "Возвращает список всех активных и непрочитанных уведомлений " +
            "авторизованного пользователя.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetNotificationsAsync(CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var notifications = await _notificationWorkflowService.GetNotificationsAsync(
                    userId,
                    ct);

                return Ok(ApiResponse.Success(notifications));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении уведомлений");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /notifications");
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }
    }
}
