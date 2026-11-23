using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.Core.DTOs.Notifications;
using Messenger.Core.Interfaces;
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
        /// Создать новое уведомление.
        /// </summary>
        [HttpPost]
        [EndpointName("CreateNotification")]
        [EndpointSummary("Создать новое уведомление")]
        [EndpointDescription("Создаёт уведомление для указанного пользователя. Обычно вызывается внутренними сервисами " +
            "(например, при новом сообщении, добавлении в чат и т.д.).")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostCreateNotificationAsync(
            [FromBody, Description("Данные для создания уведомления")] CreateNotificationRequest request,
            CancellationToken ct = default)
        {
            try
            {
                await User.GetCurrentUserIdAsync(_userService);

                await _notificationService.CreateNotificationAsync(request.UserId, request.Text, ct);

                return Ok(new
                {
                    IsSuccess = true,
                    Message = "Уведомление успешно создано"
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при создании уведомления");
                return Unauthorized(new
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new
                {
                    IsSuccess = false,
                    Error = $"Пользователь с ID {request.UserId} не найден"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении POST /notifications");
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
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
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var notifications = await _notificationService.GetNotificationsAsync(userId, ct);

                return Ok(new
                {
                    IsSuccess = true,
                    Data = notifications
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении уведомлений");
                return Unauthorized(new
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /notifications");
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }
    }
}