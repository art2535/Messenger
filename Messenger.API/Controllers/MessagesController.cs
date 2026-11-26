using Asp.Versioning;
using Messenger.API.Extensions;
using MassTransit;
using Messenger.Core.DTOs.Messages;
using Messenger.Core.Interfaces;
using Messenger.Core.Messages;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel;
using System.Security.Claims;
using Messenger.Core.Services;
using Messenger.Core.Responses;
using Messenger.Infrastructure.Interfaces.Messages;
using Messenger.Infrastructure.Services.Messages;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для управления сообщениями.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/messages")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Tags("Messages")]
    public class MessagesController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IMessageRealtimeNotifier _messageRealtimeNotifier;
        private readonly IUserService _userService;
        private readonly ILogger<MessagesController> _logger;
        private readonly SendMessageRateLimiterService _sendLimiter;
        private readonly IMessageSendingService _messageSendingService;
        private readonly IMessageForwardingService _messageForwardingService;
        private readonly IMessageEditingWorkflowService _messageEditingWorkflowService;
        private readonly IMessageDeletionWorkflowService _messageDeletionWorkflowService;
        private readonly IMessageControllerWorkflowService _messageControllerWorkflowService;
        private readonly IMessageHistoryWorkflowService _messageHistoryWorkflowService;
        private readonly IMessageSearchWorkflowService _messageSearchWorkflowService;

        public MessagesController(
            IConfiguration configuration,
            IMessageRealtimeNotifier messageRealtimeNotifier,
            IUserService userService,
            ILogger<MessagesController> logger,
            SendMessageRateLimiterService sendLimiter,
            IMessageSendingService messageSendingService,
            IMessageForwardingService messageForwardingService,
            IMessageEditingWorkflowService messageEditingWorkflowService,
            IMessageDeletionWorkflowService messageDeletionWorkflowService,
            IMessageControllerWorkflowService messageControllerWorkflowService,
            IMessageHistoryWorkflowService messageHistoryWorkflowService,
            IMessageSearchWorkflowService messageSearchWorkflowService)
        {
            _configuration = configuration;
            _messageRealtimeNotifier = messageRealtimeNotifier;
            _userService = userService;
            _logger = logger;
            _sendLimiter = sendLimiter;
            _messageSendingService = messageSendingService;
            _messageForwardingService = messageForwardingService;
            _messageEditingWorkflowService = messageEditingWorkflowService;
            _messageDeletionWorkflowService = messageDeletionWorkflowService;
            _messageControllerWorkflowService = messageControllerWorkflowService;
            _messageHistoryWorkflowService = messageHistoryWorkflowService;
            _messageSearchWorkflowService = messageSearchWorkflowService;
        }

        /// <summary>
        /// Поиск сообщений в чате.
        /// </summary>
        [HttpGet("{chatId:guid}/search")]
        [EndpointName("SearchMessages")]
        [EndpointSummary("Поиск сообщений в чате")]
        [EndpointDescription("Возвращает сообщения, соответствующие критерию поиска в указанном чате.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetSearchMessagesAsync(
            [FromRoute, Description("Идентификатор чата (GUID)")] Guid chatId,
            [FromQuery, Description("Критерий поиска")] string query,
            CancellationToken ct = default)
        {
            try
            {
                await User.GetCurrentUserIdAsync(_userService);

                var results = await _messageSearchWorkflowService.SearchAsync(
                    chatId,
                    query,
                    ct);

                return Ok(ApiResponse.Success(results));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при поиске сообщений в чате {ChatId}", chatId);
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /messages/{ChatId}/search", chatId);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Отправить сообщение в чат.
        /// </summary>
        [HttpPost("{chatId:guid}")]
        [DisableRateLimiting]
        [EndpointName("SendMessage")]
        [EndpointSummary("Отправить сообщение в чат")]
        [EndpointDescription("Отправляет текстовое сообщение и/или файлы (вложения) в указанный чат. " +
            "Сообщение сохраняется асинхронно через consumer и рассылается через SignalR.")]
        [Consumes("multipart/form-data", "application/json")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostSendMessageAsync(
            [FromRoute, Description("Идентификатор чата (GUID)")] Guid chatId,
            [FromForm, Description("Текст сообщения (опционально)")] string? messageText,
            [FromForm, Description("Файлы-вложения (опционально, несколько файлов)")] IFormFile[]? files,
            CancellationToken ct = default)
        {
            try
            {
                var rateKey = User.FindFirst("sub")?.Value
                    ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? HttpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";

                if (!await _sendLimiter.TryAcquireAsync(rateKey))
                {
                    _logger.LogWarning("SendMessage rate limit exceeded for {Key}", rateKey);
                    return StatusCode(
                        StatusCodes.Status429TooManyRequests,
                        ApiResponse.Failure(
                            "Слишком много запросов. Подождите немного и попробуйте снова.",
                            "RATE_LIMIT_EXCEEDED"));
                }

                const long maxFileSize = 10 * 1024 * 1024;
                if (files != null && files.Length > 0)
                {
                    foreach (var file in files)
                    {
                        if (file.Length > maxFileSize)
                        {
                            return BadRequest(ApiResponse.Failure(
                                $"Файл '{file.FileName}' превышает максимальный размер 10 МБ. " +
                                $"Текущий размер: {file.Length / (1024 * 1024):F2} МБ",
                                "INVALID_REQUEST"));
                        }
                    }
                }

                var userId = await User.GetCurrentUserIdAsync(_userService);
                var message = await _messageSendingService.SendAsync(
                    chatId,
                    userId,
                    messageText,
                    files,
                    ct);

                return Ok(ApiResponse.Success(message));
            }
            catch (MessageSendingException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Не удалось отправить сообщение в чат {ChatId}: {Reason}",
                    chatId,
                    ex.Failure);

                return ex.Failure switch
                {
                    MessageSendingFailure.UserNotFound => Unauthorized(
                        ApiResponse.Failure(ex.Message, "UNAUTHORIZED")),
                    MessageSendingFailure.ChatNotFound => NotFound(
                        ApiResponse.Failure(ex.Message, "NOT_FOUND")),
                    MessageSendingFailure.UserNotParticipant => StatusCode(
                        StatusCodes.Status403Forbidden,
                        ApiResponse.Failure(ex.Message, "FORBIDDEN")),
                    _ => StatusCode(
                        StatusCodes.Status500InternalServerError,
                        ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"))
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при отправке сообщения в чат {ChatId}", chatId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отправке сообщения в чат {ChatId}", chatId);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Переслать одно или несколько сообщений в другие чаты (включая вложения).
        /// </summary>
        [HttpPost("forward")]
        [EndpointName("ForwardMessages")]
        [EndpointSummary("Пересылка сообщений")]
        [EndpointDescription("Копирует выбранные сообщения (текст + файлы на диске) в указанные чаты. До 20 сообщений и 5 чатов за раз.")]
        [Consumes("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostForwardMessagesAsync(
            [FromBody, Description("Данные для пересылки сообщений")] ForwardMessagesRequest request,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);

                if (request == null)
                {
                    return BadRequest(ApiResponse.Failure(
                        "Тело запроса пустое или неверный JSON",
                        "INVALID_REQUEST"));
                }

                if (request.MessageIds == null || request.MessageIds.Count == 0)
                {
                    return BadRequest(ApiResponse.Failure(
                        "Не указаны сообщения для пересылки",
                        "INVALID_REQUEST"));
                }

                if (request.TargetChatIds == null || request.TargetChatIds.Count == 0)
                {
                    return BadRequest(ApiResponse.Failure(
                        "Не указаны целевые чаты",
                        "INVALID_REQUEST"));
                }

                if (request.MessageIds.Count > 20)
                {
                    return BadRequest(ApiResponse.Failure(
                        "Можно переслать не более 20 сообщений за раз",
                        "INVALID_REQUEST"));
                }

                if (request.TargetChatIds.Count > 5)
                {
                    return BadRequest(ApiResponse.Failure(
                        "Можно выбрать не более 5 чатов",
                        "INVALID_REQUEST"));
                }

                var messageIds = request.MessageIds
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();

                var targetChatIds = request.TargetChatIds
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();

                if (messageIds.Count == 0)
                {
                    return BadRequest(ApiResponse.Failure(
                        "Некорректные ID сообщений",
                        "INVALID_REQUEST"));
                }

                if (targetChatIds.Count == 0)
                {
                    return BadRequest(ApiResponse.Failure(
                        "Некорректные ID чатов",
                        "INVALID_REQUEST"));
                }

                var result = await _messageForwardingService.ForwardAsync(
                    userId,
                    messageIds,
                    targetChatIds,
                    ct);

                if (result.Failure != null)
                {
                    return result.Failure switch
                    {
                        MessageForwardingFailure.UserNotFound => Unauthorized(
                            ApiResponse.Failure(result.ErrorMessage, "UNAUTHORIZED")),
                        MessageForwardingFailure.SourceMessageNotFound => NotFound(
                            ApiResponse.Failure(result.ErrorMessage, "NOT_FOUND")),
                        MessageForwardingFailure.SourceChatAccessDenied => StatusCode(
                            StatusCodes.Status403Forbidden,
                            ApiResponse.Failure(result.ErrorMessage, "FORBIDDEN")),
                        MessageForwardingFailure.TargetChatNotFound => NotFound(
                            ApiResponse.Failure(result.ErrorMessage, "NOT_FOUND")),
                        MessageForwardingFailure.TargetChatAccessDenied => StatusCode(
                            StatusCodes.Status403Forbidden,
                            ApiResponse.Failure(result.ErrorMessage, "FORBIDDEN")),
                        MessageForwardingFailure.NothingPublished => StatusCode(
                            StatusCodes.Status500InternalServerError,
                            ApiResponse.Failure(result.ErrorMessage, "INTERNAL_ERROR")),
                        _ => StatusCode(
                            StatusCodes.Status500InternalServerError,
                            ApiResponse.Failure(result.ErrorMessage, "INTERNAL_ERROR"))
                    };
                }

                return Ok(ApiResponse.Success(
                    new
                    {
                        published = result.Published,
                        errors = result.Errors
                    },
                    meta: new
                    {
                        published = result.Published,
                        errorCount = result.Errors.Count
                    }));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при пересылке сообщений");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении POST /messages/forward");
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Пометить все сообщения чата как прочитанные (для текущего пользователя).
        /// </summary>
        [HttpPost("{chatId:guid}/read")]
        [EndpointName("MarkChatAsRead")]
        [EndpointSummary("Пометить все сообщения чата как прочитанные")]
        [EndpointDescription("Обновляет статус сообщения на прочитанный")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostMarkChatAsReadAsync(
            [FromRoute, Description("Идентификатор чата")] Guid chatId,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var result = await _messageControllerWorkflowService.MarkChatAsReadAsync(
                    chatId,
                    userId,
                    ct);

                return result.Status switch
                {
                    MessageWorkflowStatus.Success => Ok(ApiResponse.Success(
                        new { updatedCount = result.UpdatedCount },
                        message: "Сообщения обновлены")),
                    MessageWorkflowStatus.NotFound => NotFound(
                        ApiResponse.Failure("Чат не найден", "NOT_FOUND")),
                    MessageWorkflowStatus.Forbidden => StatusCode(
                        StatusCodes.Status403Forbidden,
                        ApiResponse.Failure("Вы не являетесь участником этого чата", "FORBIDDEN")),
                    _ => StatusCode(
                        StatusCodes.Status500InternalServerError,
                        ApiResponse.Failure("Не удалось обновить сообщения", "INTERNAL_ERROR"))
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Ошибка авторизации при отметке прочтения в чате {ChatId}",
                    chatId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении POST /messages/{ChatId}/read", chatId);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        
        /// <summary>
        /// Получить сообщения чата.
        /// </summary>
        [HttpGet("{chatId:guid}")]
        [EndpointName("GetMessagesByChat")]
        [EndpointSummary("Получить сообщения чата")]
        [EndpointDescription(
            "Возвращает страницу сообщений. Параметр beforeSequence — SequenceNumber самого старого уже загруженного сообщения " +
            "(для подгрузки истории). Без параметра возвращаются последние limit сообщений.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetMessagesByChatAsync(
            [FromRoute, Description("Идентификатор чата (GUID)")] Guid chatId,
            [FromQuery, Description("Загрузить сообщения старше этого SequenceNumber")] long? beforeSequence = null,
            [FromQuery, Description("Размер страницы (1–200, по умолчанию 50)")] int limit = 50,
            CancellationToken ct = default)
        {
            try
            {
                await User.GetCurrentUserIdAsync(_userService);

                var result = await _messageHistoryWorkflowService.GetMessagesAsync(
                    chatId,
                    beforeSequence,
                    limit,
                    ct);

                return Ok(ApiResponse.Success(result.Messages, meta: new
                {
                    hasMore = result.HasMore,
                    nextBeforeSequence = result.NextBeforeSequence
                }));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении сообщений чата {ChatId}", chatId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /messages/{ChatId}", chatId);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Экспорт истории чата в разных форматах.
        /// </summary>
        [HttpGet("{chatId:guid}/export")]
        [EndpointName("ExportChat")]
        [EndpointSummary("Экспорт истории чата")]
        [EndpointDescription(
            "Скачивает историю сообщений чата в одном из форматов: txt, json, html, csv. " +
            "Доступно только участникам чата. Максимум 5000 сообщений.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetExportChatAsync(
            [FromRoute, Description("Идентификатор чата (GUID)")] Guid chatId,
            [FromQuery, Description("Формат экспорта: txt | json | html | csv (по умолчанию txt)")] string format = "txt",
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);

                var result = await _messageHistoryWorkflowService.ExportChatAsync(
                    chatId,
                    userId,
                    format,
                    ct);

                return result.Status switch
                {
                    MessageHistoryStatus.Success => File(
                        result.Export!.Content,
                        result.Export.ContentType,
                        result.Export.FileName),
                    MessageHistoryStatus.Forbidden => StatusCode(
                        StatusCodes.Status403Forbidden,
                        ApiResponse.Failure(
                            "Вы не являетесь участником этого чата",
                            "FORBIDDEN")),
                    MessageHistoryStatus.NotFound => NotFound(
                        ApiResponse.Failure("Чат не найден", "NOT_FOUND")),
                    MessageHistoryStatus.InvalidFormat => BadRequest(
                        ApiResponse.Failure(
                            "Поддерживаемые форматы: txt, json, html, csv",
                            "INVALID_REQUEST")),
                    _ => StatusCode(
                        StatusCodes.Status500InternalServerError,
                        ApiResponse.Failure(
                            "Не удалось экспортировать историю чата",
                            "INTERNAL_ERROR"))
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при экспорте чата {ChatId}", chatId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /messages/{ChatId}/export", chatId);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Редактировать сообщение.
        /// </summary>
        [HttpPut("{messageId:guid}")]
        [EndpointName("UpdateMessage")]
        [EndpointSummary("Редактировать сообщение")]
        [EndpointDescription("Изменяет текст существующего сообщения. Доступно только отправителю.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PutUpdateMessageAsync(
            [FromRoute, Description("Идентификатор сообщения (GUID)")] Guid messageId,
            [FromBody, Description("Новый текст сообщения и ID чата")] UpdateMessageRequest request,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(request.MessageText))
            {
                return BadRequest(ApiResponse.Failure("Текст не может быть пустым", "INVALID_REQUEST"));
            }

            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var result = await _messageEditingWorkflowService.EditAsync(
                    request.ChatId,
                    messageId,
                    userId,
                    request.MessageText,
                    ct);

                return result.Status switch
                {
                    MessageEditStatus.Updated => Ok(ApiResponse.Success(message: "Сообщение обновлено")),
                    MessageEditStatus.NotFound => NotFound(
                        ApiResponse.Failure("Сообщение не найдено", "NOT_FOUND")),
                    MessageEditStatus.Forbidden => StatusCode(
                        StatusCodes.Status403Forbidden,
                        ApiResponse.Failure("Редактировать можно только своё сообщение", "FORBIDDEN")),
                    _ => StatusCode(
                        StatusCodes.Status500InternalServerError,
                        ApiResponse.Failure("Не удалось обновить сообщение", "INTERNAL_ERROR"))
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при редактировании сообщения {MessageId}", messageId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении PUT /messages/{MessageId}", messageId);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Удалить сообщение.
        /// </summary>
        [HttpDelete("{messageId:guid}")]
        [EndpointName("DeleteMessage")]
        [EndpointSummary("Удалить сообщение")]
        [EndpointDescription("Удаляет сообщение. Доступно только отправителю. Уведомление рассылается через SignalR.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteMessageAsync(
            [FromRoute, Description("Идентификатор сообщения (GUID)")] Guid messageId,
            [FromQuery, Description("Идентификатор чата (обязателен для проверки прав)")] Guid chatId,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var result = await _messageDeletionWorkflowService.DeleteAsync(
                    chatId,
                    messageId,
                    userId,
                    ct);

                return result switch
                {
                    MessageDeletionStatus.Deleted => Ok(
                        ApiResponse.Success(message: "Сообщение удалено")),
                    MessageDeletionStatus.NotFound => NotFound(
                        ApiResponse.Failure("Сообщение не найдено", "NOT_FOUND")),
                    MessageDeletionStatus.Forbidden => StatusCode(
                        StatusCodes.Status403Forbidden,
                        ApiResponse.Failure("Вы не являетесь участником этого чата", "FORBIDDEN")),
                    _ => StatusCode(
                        StatusCodes.Status500InternalServerError,
                        ApiResponse.Failure("Не удалось удалить сообщение", "INTERNAL_ERROR"))
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при удалении сообщения {MessageId}", messageId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении DELETE /messages/{MessageId}", messageId);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Удалить несколько сообщений.
        /// </summary>
        [HttpPost("bulk-delete")]
        [EndpointName("BulkDeleteMessages")]
        [EndpointSummary("Удалить несколько сообщений")]
        [EndpointDescription("Удаляет указанные сообщения. Можно удалить только свои сообщения. Уведомления рассылаются через SignalR.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostBulkDeleteMessagesAsync(
            [FromBody, Description("Данные для массового удаления сообщений")] BulkDeleteMessagesRequest request,
            CancellationToken ct = default)
        {
            try
            {
                await User.GetCurrentUserIdAsync(_userService);

                if (request == null || request.MessageIds == null || request.MessageIds.Count == 0)
                {
                    return BadRequest(ApiResponse.Failure(
                        "Не указаны сообщения для удаления",
                        "INVALID_REQUEST"));
                }

                if (request.MessageIds.Count > 100)
                {
                    return BadRequest(ApiResponse.Failure(
                        "За один раз можно удалить не более 100 сообщений",
                        "INVALID_REQUEST"));
                }

                var result = await _messageControllerWorkflowService.DeleteManyAsync(
                    request.ChatId,
                    request.MessageIds,
                    ct);

                return result.Status switch
                {
                    MessageWorkflowStatus.Success => Ok(ApiResponse.Success(
                        new { deletedCount = result.DeletedCount, messageIds = result.MessageIds },
                        message: "Сообщения удалены")),
                    MessageWorkflowStatus.NotFound => NotFound(
                        ApiResponse.Failure("Сообщения не найдены", "NOT_FOUND")),
                    _ => StatusCode(
                        StatusCodes.Status500InternalServerError,
                        ApiResponse.Failure("Не удалось удалить сообщения", "INTERNAL_ERROR"))
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при массовом удалении сообщений");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Ошибка при выполнении POST /messages/bulk-delete");
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        
        /// <summary>
        /// Закрепить сообщение в чате.
        /// </summary>
        [HttpPost("{messageId:guid}/pin")]
        [EndpointName("PinMessage")]
        [EndpointSummary("Закрепить сообщение")]
        [EndpointDescription("Закрепляет сообщение в чате. Доступно любому участнику чата.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostPinMessageAsync(
            [FromRoute, Description("Идентификатор сообщения (GUID)")] Guid messageId,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var result = await _messageControllerWorkflowService.SetPinnedAsync(
                    messageId,
                    userId,
                    true,
                    ct);

                return result.Status switch
                {
                    MessageWorkflowStatus.Success => Ok(ApiResponse.Success(
                        result.Payload,
                        "Сообщение закреплено")),
                    MessageWorkflowStatus.NotFound => NotFound(
                        ApiResponse.Failure("Сообщение не найдено", "NOT_FOUND")),
                    MessageWorkflowStatus.Forbidden => StatusCode(
                        StatusCodes.Status403Forbidden,
                        ApiResponse.Failure(
                            "Недостаточно прав для закрепления сообщения",
                            "FORBIDDEN")),
                    _ => StatusCode(
                        StatusCodes.Status500InternalServerError,
                        ApiResponse.Failure("Не удалось закрепить сообщение", "INTERNAL_ERROR"))
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Ошибка авторизации при закреплении сообщения {MessageId}",
                    messageId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Ошибка при выполнении POST /messages/{MessageId}/pin",
                    messageId);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        
        /// <summary>
        /// Открепить сообщение.
        /// </summary>
        [HttpDelete("{messageId:guid}/pin")]
        [EndpointName("UnpinMessage")]
        [EndpointSummary("Открепить сообщение")]
        [EndpointDescription("Снимает закрепление с сообщения. Доступно любому участнику чата.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteUnpinMessageAsync(
            [FromRoute, Description("Идентификатор сообщения (GUID)")] Guid messageId,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var result = await _messageControllerWorkflowService.SetPinnedAsync(
                    messageId,
                    userId,
                    false,
                    ct);

                return result.Status switch
                {
                    MessageWorkflowStatus.Success => Ok(ApiResponse.Success(
                        result.Payload,
                        "Сообщение откреплено")),
                    MessageWorkflowStatus.NotFound => NotFound(
                        ApiResponse.Failure("Сообщение не найдено", "NOT_FOUND")),
                    MessageWorkflowStatus.Forbidden => StatusCode(
                        StatusCodes.Status403Forbidden,
                        ApiResponse.Failure(
                            "Недостаточно прав для открепления сообщения",
                            "FORBIDDEN")),
                    _ => StatusCode(
                        StatusCodes.Status500InternalServerError,
                        ApiResponse.Failure("Не удалось открепить сообщение", "INTERNAL_ERROR"))
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Ошибка авторизации при откреплении сообщения {MessageId}",
                    messageId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Ошибка при выполнении DELETE /messages/{MessageId}/pin",
                    messageId);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        
        /// <summary>
        /// Получить закреплённые сообщения чата.
        /// </summary>
        [HttpGet("{chatId:guid}/pinned")]
        [EndpointName("GetPinnedMessages")]
        [EndpointSummary("Список закреплённых сообщений")]
        [EndpointDescription("Возвращает все закреплённые сообщения указанного чата (от старых к новым).")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetPinnedMessagesAsync(
            [FromRoute, Description("Идентификатор чата (GUID)")] Guid chatId,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var result = await _messageControllerWorkflowService.GetPinnedAsync(
                    chatId,
                    userId,
                    ct);

                return result.Status switch
                {
                    MessageWorkflowStatus.Success => Ok(ApiResponse.Success(result.Messages)),
                    MessageWorkflowStatus.Forbidden => StatusCode(
                        StatusCodes.Status403Forbidden,
                        ApiResponse.Failure("Вы не являетесь участником этого чата", "FORBIDDEN")),
                    _ => StatusCode(
                        StatusCodes.Status500InternalServerError,
                        ApiResponse.Failure(
                            "Не удалось получить закреплённые сообщения",
                            "INTERNAL_ERROR"))
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Ошибка авторизации при получении закреплённых сообщений чата {ChatId}",
                    chatId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /messages/{ChatId}/pinned", chatId);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        
    }
}