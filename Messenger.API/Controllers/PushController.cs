using Asp.Versioning;
using Messenger.Core.DTOs.Push;
using Messenger.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;
using System.Security.Claims;
using WebPush;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Push-уведомления — управление подписками, настройками и отправкой веб-push
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Tags("Push")]
    public class PushController : ControllerBase
    {
        private readonly IPushSubscriptionService _subscriptionService;
        private readonly VapidDetails _vapidDetails;
        private readonly ILogger<PushController> _logger;
        private readonly IUserService _userService;
        private readonly INotificationService _notificationService;

        public PushController(IPushSubscriptionService subscriptionService, IConfiguration configuration,
            ILogger<PushController> logger, IUserService userService, INotificationService notificationService)
        {
            _subscriptionService = subscriptionService;
            _logger = logger;

            var vapidSection = configuration.GetSection("Vapid");

            _vapidDetails = new VapidDetails(vapidSection["Subject"], vapidSection["PublicKey"]!, 
                vapidSection["PrivateKey"]!);

            _userService = userService;
            _notificationService = notificationService;
        }

        /// <summary>
        /// Подписать пользователя на push-уведомления
        /// </summary>
        [HttpPost("subscribe")]
        [EndpointName("SubscribeToPush")]
        [EndpointSummary("Подписать пользователя на push-уведомления")]
        [EndpointDescription("Сохраняет push-подписку браузера для последующей отправки веб-уведомлений. " +
            "Если подписка с таким Endpoint уже существует — она будет заменена.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [Consumes("application/json")]
        public async Task<IActionResult> SubscribeAsync(
            [FromBody, Description("Данные для создания подписки")] PushSubscriptionRequest subscriptionDto)
        {
            if (subscriptionDto == null || string.IsNullOrEmpty(subscriptionDto.Endpoint))
                return BadRequest(new { error = "Некорректные данные подписки" });

            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                           ?? User.FindFirstValue("sub");

            var user = await _userService.GetUserByExternalIdAsync(userIdClaim);

            if (string.IsNullOrEmpty(subscriptionDto.P256dh) || string.IsNullOrEmpty(subscriptionDto.Auth))
            {
                _logger.LogWarning("Подписка от пользователя {UserId} пришла без p256dh/auth ключей", user!.UserId);
                return BadRequest(new { error = "Отсутствуют ключи шифрования p256dh или auth" });
            }

            var subscription = new Core.Models.PushSubscription
            {
                UserId = user!.UserId,
                Endpoint = subscriptionDto.Endpoint,
                P256dh = subscriptionDto.P256dh,
                Auth = subscriptionDto.Auth,
                CreatedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow
            };

            await _subscriptionService.RemoveByEndpointAsync(subscriptionDto.Endpoint);

            await _subscriptionService.AddSubscriptionAsync(subscription);
            await _subscriptionService.EnsurePushEnabledAsync(user!.UserId);

            _logger.LogInformation("Push-подписка успешно сохранена для пользователя {UserId}. P256dh length: {Len}",
                user!.UserId, subscription.P256dh.Length);

            return Ok(new { message = "Подписка успешно сохранена" });
        }

        /// <summary>
        /// Отписать пользователя от push-уведомлений
        /// </summary>
        [HttpDelete("unsubscribe")]
        [EndpointName("UnsubscribeFromPush")]
        [EndpointSummary("Отписать пользователя от push-уведомлений")]
        [EndpointDescription("Удаляет push-подписку по указанному endpoint.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [Consumes("application/json")]
        public async Task<IActionResult> UnsubscribeAsync(
            [FromBody, Description("Endpoint для удаления подписки")] string endpoint)
        {
            if (string.IsNullOrEmpty(endpoint))
                return BadRequest();

            await _subscriptionService.RemoveByEndpointAsync(endpoint);

            return Ok(new { message = "Подписка удалена" });
        }

        /// <summary>
        /// Пометить уведомление как прочитанное
        /// </summary>
        [HttpPost("{notificationId}/read")]
        [EndpointName("MarkNotificationAsRead")]
        [EndpointSummary("Пометить уведомление как прочитанное")]
        [EndpointDescription("Отмечает конкретное push-уведомление как прочитанное для текущего пользователя.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> MarkAsReadAsync([Description("Идентификатор уведомления")] Guid notificationId)
        {
            await _notificationService.MarkAsReadAsync(notificationId);

            return Ok(new { message = "Уведомление помечено как прочитанное" });
        }

        /// <summary>
        /// Получить настройки push-уведомлений текущего пользователя
        /// </summary>
        [HttpGet("settings")]
        [EndpointName("GetPushSettings")]
        [EndpointSummary("Получить настройки push-уведомлений")]
        [EndpointDescription("Возвращает текущие настройки push-уведомлений (включены ли уведомления, о сообщениях, группах и упоминаниях).")]
        [ProducesResponseType(typeof(PushSubscriptionUpdateRequest), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetSettingsAsync(CancellationToken token = default)
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            var user = await _userService.GetUserByExternalIdAsync(userIdClaim);

            if (user == null)
                return Ok(new PushSubscriptionUpdateRequest());

            var settings = await _subscriptionService.GetPushSettingsAsync(user.UserId, token);

            if (settings == null)
            {
                return Ok(new PushSubscriptionUpdateRequest
                {
                    PushEnabled = true,
                    NotifyMessages = true,
                    NotifyGroupChats = true,
                    NotifyMentions = true
                });
            }

            return Ok(new PushSubscriptionUpdateRequest
            {
                PushEnabled = settings.PushEnabled,
                NotifyMessages = settings.NotifyMessages,
                NotifyGroupChats = settings.NotifyGroupChats,
                NotifyMentions = settings.NotifyMentions
            });
        }

        /// <summary>
        /// Сохранить настройки push-уведомлений
        /// </summary>
        [HttpPost("settings")]
        [EndpointName("SavePushSettings")]
        [EndpointSummary("Сохранить настройки push-уведомлений")]
        [EndpointDescription("Обновляет настройки push-уведомлений пользователя " +
            "(включение/отключение всех уведомлений, сообщений, групп и упоминаний).")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> SaveSettingsAsync(
            [FromBody, Description("Настройки push-уведомлений")] PushSubscriptionUpdateRequest request,
            CancellationToken token = default)
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            var user = await _userService.GetUserByExternalIdAsync(userIdClaim);

            if (user == null)
                return Unauthorized();

            await _subscriptionService.SavePushSettingsAsync(user.UserId, request, token);

            _logger.LogInformation("Настройки push-уведомлений обновлены для пользователя {UserId}", user.UserId);

            return Ok(new { message = "Настройки push-уведомлений успешно сохранены" });
        }

        /// <summary>
        /// Отправить push-уведомления участникам чата
        /// </summary>
        [HttpPost("send")]
        [EndpointName("SendPushNotification")]
        [EndpointSummary("Отправить push-уведомления участникам чата")]
        [EndpointDescription("Отправляет веб-push уведомления всем участникам чата (кроме отправителя), у которых включены соответствующие настройки. Вызывается после отправки сообщения.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SendPushNotificationAsync(
            [FromBody, Description("Данные для отправки уведомления")] SendPushNotificationRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null || request.ChatId == Guid.Empty)
                return BadRequest(new { error = "Некорректные данные запроса" });

            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                           ?? User.FindFirstValue("sub");

            var sender = await _userService.GetUserByExternalIdAsync(userIdClaim);
            if (sender == null)
                return Unauthorized();

            try
            {
                await _subscriptionService.SendPushToOfflineUsersAsync(request.ChatId, sender.UserId,
                    request.SenderName ?? "Пользователь", request.MessageText, request.HasAttachments,
                    request.IsMention, cancellationToken);

                return Ok(new { message = "Push-уведомления обработаны" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке push-уведомлений для чата {ChatId}", request.ChatId);
                return StatusCode(500, new { error = "Внутренняя ошибка сервера при отправке push" });
            }
        }
        /// <summary>
        /// Получить публичный VAPID-ключ
        /// </summary>
        [HttpGet("vapid-public-key")]
        [AllowAnonymous]
        [EndpointName("GetVapidPublicKey")]
        [EndpointSummary("Получить публичный VAPID-ключ")]
        [EndpointDescription("Возвращает публичный VAPID-ключ, необходимый на клиентской стороне для создания push-подписки " +
            "через Service Worker.")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        public IActionResult GetVapidPublicKey()
        {
            var publicKey = _vapidDetails.PublicKey;
            return Content(publicKey, "text/plain");
        }        
    }
}
