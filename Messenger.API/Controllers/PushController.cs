using Messenger.Application.DTOs.Responses;
﻿using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.Application.DTOs.Push;
using Messenger.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;
using WebPush;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Push-уведомления — управление подписками, настройками и отправкой веб-push.
    /// </summary>
    [ApiController]
    [Route("api/v{version:apiVersion}/push")]
    [ApiVersion("1.0")]
    [Authorize]
    [Produces("application/json")]
    [Consumes("application/json")]
    [Tags("Push")]
    public class PushController : ControllerBase
    {
        private readonly IPushSubscriptionService _subscriptionService;
        private readonly VapidDetails _vapidDetails;
        private readonly ILogger<PushController> _logger;
        private readonly IUserService _userService;
        private readonly INotificationService _notificationService;

        public PushController(
            IPushSubscriptionService subscriptionService,
            IConfiguration configuration,
            ILogger<PushController> logger,
            IUserService userService,
            INotificationService notificationService)
        {
            _subscriptionService = subscriptionService;
            _logger = logger;
            _userService = userService;
            _notificationService = notificationService;

            var vapidSection = configuration.GetSection("Vapid");
            _vapidDetails = new VapidDetails(
                vapidSection["Subject"],
                vapidSection["PublicKey"]!,
                vapidSection["PrivateKey"]!);
        }

        [HttpPost("subscribe")]
        [EndpointName("SubscribeToPush")]
        [EndpointSummary("Подписать пользователя на push-уведомления")]
        [EndpointDescription("Сохраняет push-подписку браузера для последующей отправки веб-уведомлений. Если подписка с таким Endpoint уже существует — она будет заменена.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostSubscribeAsync(
            [FromBody, Description("Данные для создания подписки")] CreatePushSubscriptionDto subscriptionDto,
            CancellationToken ct = default)
        {
            try
            {
                if (subscriptionDto == null || string.IsNullOrEmpty(subscriptionDto.Endpoint))
                {
                    return BadRequest(ApiResponse.Failure("Некорректные данные подписки", "INVALID_REQUEST"));
                }

                var userId = await User.GetCurrentUserIdAsync(_userService, ct);

                if (string.IsNullOrEmpty(subscriptionDto.P256dh) || string.IsNullOrEmpty(subscriptionDto.Auth))
                {
                    _logger.LogWarning("Подписка от пользователя {UserId} пришла без p256dh/auth ключей", userId);
                    return BadRequest(ApiResponse.Failure("Отсутствуют ключи шифрования p256dh или auth", "INVALID_REQUEST"));
                }

                await _subscriptionService.RemoveByEndpointAsync(subscriptionDto.Endpoint, ct);
                await _subscriptionService.AddSubscriptionAsync(userId, subscriptionDto, ct);
                await _subscriptionService.EnsurePushEnabledAsync(userId, ct);

                _logger.LogInformation("Push-подписка успешно сохранена для пользователя {UserId}. P256dh length: {Len}",
                    userId, subscriptionDto.P256dh.Length);

                return Ok(ApiResponse.Success(message: "Подписка успешно сохранена"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при подписке на push-уведомления");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при сохранении push-подписки");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        [HttpDelete("unsubscribe")]
        [EndpointName("UnsubscribeFromPush")]
        [EndpointSummary("Отписать пользователя от push-уведомлений")]
        [EndpointDescription("Удаляет push-подписку по указанному endpoint.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteUnsubscribeAsync(
            [FromBody, Description("Endpoint для удаления подписки")] string endpoint,
            CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrEmpty(endpoint))
                {
                    return BadRequest(ApiResponse.Failure("Endpoint не может быть пустым", "INVALID_REQUEST"));
                }

                await _subscriptionService.RemoveByEndpointAsync(endpoint, ct);

                return Ok(ApiResponse.Success(message: "Подписка удалена"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении push-подписки");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        [HttpPost("{notificationId}/read")]
        [EndpointName("MarkNotificationAsRead")]
        [EndpointSummary("Пометить уведомление как прочитанное")]
        [EndpointDescription("Отмечает конкретное push-уведомление как прочитанное для текущего пользователя.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostMarkAsReadAsync(
            [Description("Идентификатор уведомления")] Guid notificationId,
            CancellationToken ct = default)
        {
            try
            {
                await _notificationService.MarkAsReadAsync(notificationId, ct);

                return Ok(ApiResponse.Success(message: "Уведомление помечено как прочитанное"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при пометке уведомления {NotificationId} как прочитанного", notificationId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        [HttpGet("settings")]
        [EndpointName("GetPushSettings")]
        [EndpointSummary("Получить настройки push-уведомлений текущего пользователя")]
        [EndpointDescription("Возвращает текущие настройки push-уведомлений (включены ли уведомления, о сообщениях, группах и упоминаниях).")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetSettingsAsync(CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);
                var settings = await _subscriptionService.GetPushSettingsAsync(userId, ct);

                var result = settings ?? new PushSettingsDto();

                return Ok(ApiResponse.Success(result));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении настроек push-уведомлений");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении настроек push-уведомлений");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        [HttpPost("settings")]
        [EndpointName("SavePushSettings")]
        [EndpointSummary("Сохранить настройки push-уведомлений")]
        [EndpointDescription("Обновляет настройки push-уведомлений пользователя (включение/отключение всех уведомлений, сообщений, групп и упоминаний).")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostSaveSettingsAsync(
            [FromBody, Description("Настройки push-уведомлений")] UpdatePushSettingsDto request,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);
                await _subscriptionService.SavePushSettingsAsync(userId, request, ct);

                _logger.LogInformation("Настройки push-уведомлений обновлены для пользователя {UserId}", userId);

                return Ok(ApiResponse.Success(message: "Настройки push-уведомлений успешно сохранены"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при сохранении настроек push-уведомлений");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при сохранении настроек push-уведомлений");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        [HttpPost("send")]
        [EndpointName("SendPushNotification")]
        [EndpointSummary("Отправить push-уведомления участникам чата")]
        [EndpointDescription("Отправляет веб-push уведомления всем участникам чата (кроме отправителя), у которых включены соответствующие настройки. Вызывается после отправки сообщения.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostSendPushNotificationAsync(
            [FromBody, Description("Данные для отправки уведомления")] SendPushNotificationDto request,
            CancellationToken ct = default)
        {
            try
            {
                if (request == null || request.ChatId == Guid.Empty)
                {
                    return BadRequest(ApiResponse.Failure("Некорректные данные запроса", "INVALID_REQUEST"));
                }

                var senderId = await User.GetCurrentUserIdAsync(_userService, ct);

                await _subscriptionService.SendPushToOfflineUsersAsync(
                    request.ChatId,
                    senderId,
                    request.SenderName ?? "Пользователь",
                    request.MessageText,
                    request.HasAttachments,
                    request.IsMention,
                    ct);

                return Ok(ApiResponse.Success(message: "Push-уведомления обработаны"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при отправке push-уведомлений");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке push-уведомлений для чата {ChatId}", request?.ChatId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        [HttpGet("vapid-public-key")]
        [AllowAnonymous]
        [EndpointName("GetVapidPublicKey")]
        [EndpointSummary("Получить публичный VAPID-ключ")]
        [EndpointDescription("Возвращает публичный VAPID-ключ, необходимый на клиентской стороне для создания push-подписки через Service Worker.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetVapidPublicKeyAsync()
        {
            return Ok(ApiResponse.Success(data: _vapidDetails.PublicKey));
        }
    }
}
