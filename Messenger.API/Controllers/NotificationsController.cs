using Asp.Versioning;
using Messenger.API.Responses;
using Messenger.API.Services;
using Messenger.Core.DTOs.Notifications;
using Messenger.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для управления уведомлениями
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

        public NotificationsController(INotificationService notificationService, IUserService userService)
        {
            _notificationService = notificationService;
            _userService = userService;
        }

        /// <summary>
        /// Создать новое уведомление
        /// </summary>
        [HttpPost]
        [EndpointName("CreateNotification")]
        [EndpointSummary("Создать новое уведомление")]
        [EndpointDescription("Создаёт уведомление для указанного пользователя. Обычно вызывается внутренними сервисами " +
            "(например, при новом сообщении, добавлении в чат и т.д.).")]
        [ProducesResponseType(typeof(CreateNotificationSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateNotificationAsync(
            [FromBody, Description("Данные для создания уведомления")] CreateNotificationRequest request, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                await _notificationService.CreateNotificationAsync(request.UserId, request.Text, cancellationToken);

                return Ok(new CreateNotificationSuccessResponse
                { 
                    IsSuccess = true, 
                    Message = "Уведомление успешно создано" 
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
            catch (KeyNotFoundException)
            {
                return NotFound(new ErrorResponse
                {
                    IsSuccess = false,
                    Error = $"Пользователь с ID {request.UserId} не найден"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Получить уведомления текущего пользователя
        /// </summary>
        [HttpGet]
        [EndpointName("GetNotifications")]
        [EndpointSummary("Получить уведомления текущего пользователя")]
        [EndpointDescription("Возвращает список всех активных и непрочитанных уведомлений авторизованного пользователя.")]
        [ProducesResponseType(typeof(GetNotificationsSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetNotificationsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                {
                    return error;
                }

                var notifications = await _notificationService.GetNotificationsAsync(user!.UserId, cancellationToken);

                return Ok(new GetNotificationsSuccessResponse
                { 
                    IsSuccess = true,
                    Data = notifications 
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new 
                { 
                    IsSuccess = false, 
                    Error = ex.Message 
                });
            }
        }
    }
}
