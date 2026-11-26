using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.Core.DTOs.Messages;
using Messenger.Core.Hubs;
using Messenger.Core.Interfaces;
using Messenger.Core.Messages;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.ComponentModel;
using Messenger.Core.Responses;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Запланированные (отложенные) сообщения.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/scheduled-messages")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Tags("ScheduledMessages")]
    public class ScheduledMessagesController : ControllerBase
    {
        private readonly IScheduledMessageService _scheduledService;
        private readonly IChatService _chatService;
        private readonly IUserService _userService;
        private readonly IEncryptionService _encryption;
        private readonly IConfiguration _configuration;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ILogger<ScheduledMessagesController> _logger;

        public ScheduledMessagesController(
            IScheduledMessageService scheduledService,
            IChatService chatService,
            IUserService userService,
            IEncryptionService encryption,
            IConfiguration configuration,
            IHubContext<ChatHub> hubContext,
            ILogger<ScheduledMessagesController> logger)
        {
            _scheduledService = scheduledService;
            _chatService = chatService;
            _userService = userService;
            _encryption = encryption;
            _configuration = configuration;
            _hubContext = hubContext;
            _logger = logger;
        }

        /// <summary>
        /// Запланировать сообщение в чат (текст + опционально файлы).
        /// </summary>
        [HttpPost("{chatId:guid}")]
        [Consumes("multipart/form-data", "application/json")]
        [EndpointName("ScheduleMessage")]
        [EndpointSummary("Запланировать сообщение в чат")]
        [EndpointDescription("Создаёт отложенное сообщение в указанном чате с опциональным прикреплением файлов.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostScheduleAsync(
            [Description("Идентификатор чата")] Guid chatId,
            [FromForm] string? messageText,
            [FromForm] DateTimeOffset? scheduledAt,
            [FromForm] string? replyToMessageId,
            [FromForm] IFormFile[]? files,
            CancellationToken ct = default)
        {
            try
            {
                var user = await User.GetCurrentUserAsync(_userService);

                if (!scheduledAt.HasValue)
                {
                    return BadRequest(ApiResponse.Failure("Не указано время отправки (scheduledAt)", "INVALID_REQUEST"));
                }

                var scheduledUtc = scheduledAt.Value.UtcDateTime;
                if (scheduledUtc <= DateTime.UtcNow.AddSeconds(30))
                {
                    return BadRequest(ApiResponse.Failure("Время отправки должно быть хотя бы на 30 секунд в будущем", "INVALID_REQUEST"));
                }

                var chat = await _chatService.GetChatByIdAsync(chatId, ct);
                if (chat == null)
                {
                    return NotFound(ApiResponse.Failure("Чат не найден", "NOT_FOUND"));
                }

                if (!chat.ChatParticipants.Any(p => p.UserId == user.UserId))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Failure("Доступ запрещён", "FORBIDDEN"));
                }

                const long MAX_FILE_SIZE = 10 * 1024 * 1024;
                if (files != null)
                {
                    foreach (var file in files)
                    {
                        if (file.Length > MAX_FILE_SIZE)
                        {
                            return BadRequest(ApiResponse.Failure($"Файл '{file.FileName}' превышает 10 МБ", "INVALID_REQUEST"));
                        }
                    }
                }

                var attachmentsInfo = new List<AttachmentInfo>();
                if (files != null && files.Length > 0)
                {
                    var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                    Directory.CreateDirectory(uploadPath);

                    foreach (var file in files.Where(f => f.Length > 0))
                    {
                        var fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
                        var filePath = Path.Combine(uploadPath, fileName);
                        var fileUrl = $"{_configuration["URL:API:HTTPS"]}/uploads/{fileName}";

                        await using var stream = new FileStream(filePath, FileMode.Create);
                        await file.CopyToAsync(stream, ct);

                        attachmentsInfo.Add(new AttachmentInfo
                        {
                            AttachmentId = Guid.NewGuid(),
                            FileName = file.FileName,
                            FileType = file.ContentType ?? "application/octet-stream",
                            SizeInBytes = file.Length,
                            Url = fileUrl
                        });
                    }
                }

                if (string.IsNullOrWhiteSpace(messageText) && attachmentsInfo.Count == 0)
                {
                    return BadRequest(ApiResponse.Failure("Пустое сообщение", "INVALID_REQUEST"));
                }

                var encrypted = string.IsNullOrWhiteSpace(messageText)
                    ? null
                    : _encryption.Encrypt(messageText.Trim());

                var entity = await _scheduledService.CreateAsync(chatId, user.UserId, encrypted,
                    attachmentsInfo.Count > 0, attachmentsInfo, scheduledUtc, replyToMessageId, ct);

                var senderName = $"{user.FirstName} {user.LastName}".Trim();
                var dto = _scheduledService.ToDto(entity, messageText?.Trim(), senderName, chat.Name);

                try
                {
                    await _hubContext.Clients.Group($"User_{user.UserId}")
                        .SendAsync("ScheduledMessageCreated", dto, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SignalR ScheduledMessageCreated failed");
                }

                return Ok(ApiResponse.Success(dto));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при планировании сообщения");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Schedule message failed for chat {ChatId}", chatId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Список всех ожидающих запланированных сообщений текущего пользователя.
        /// </summary>
        [HttpGet]
        [EndpointName("ListMineScheduledMessages")]
        [EndpointSummary("Список ожидающих запланированных сообщений")]
        [EndpointDescription("Возвращает все отложенные сообщения текущего пользователя, которые ещё не были отправлены.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetListMineAsync(CancellationToken ct = default)
        {
            try
            {
                var user = await User.GetCurrentUserAsync(_userService);

                var list = await _scheduledService.GetPendingByUserAsync(user.UserId, ct);
                var dtos = list.Select(e =>
                {
                    string? plain = null;
                    if (!string.IsNullOrEmpty(e.MessageText))
                        plain = _encryption.TryDecryptSafe(e.MessageText);
                    return _scheduledService.ToDto(e, plain, null, e.Chat?.Name);
                }).ToList();

                return Ok(ApiResponse.Success(dtos));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении списка запланированных сообщений");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении списка запланированных сообщений пользователя");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Запланированные сообщения в конкретном чате (только свои pending).
        /// </summary>
        [HttpGet("chat/{chatId:guid}")]
        [EndpointName("ListScheduledMessagesByChat")]
        [EndpointSummary("Запланированные сообщения в чате")]
        [EndpointDescription("Возвращает отложенные сообщения текущего пользователя в указанном чате.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetListByChatAsync(
            [Description("Идентификатор чата")] Guid chatId,
            CancellationToken ct = default)
        {
            try
            {
                var user = await User.GetCurrentUserAsync(_userService);

                var list = await _scheduledService.GetPendingByChatAsync(chatId, user.UserId, ct);
                var dtos = list.Select(e =>
                {
                    string? plain = null;
                    if (!string.IsNullOrEmpty(e.MessageText))
                        plain = _encryption.TryDecryptSafe(e.MessageText);
                    return _scheduledService.ToDto(e, plain);
                }).ToList();

                return Ok(ApiResponse.Success(dtos));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении запланированных сообщений чата {ChatId}", chatId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении запланированных сообщений чата {ChatId}", chatId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Обновить текст или время запланированного сообщения.
        /// </summary>
        [HttpPut("{scheduledMessageId:guid}")]
        [Consumes("application/json")]
        [EndpointName("UpdateScheduledMessage")]
        [EndpointSummary("Обновить запланированное сообщение")]
        [EndpointDescription("Изменяет текст или время отправки ранее запланированного сообщения.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PutUpdateAsync(
            [Description("Идентификатор запланированного сообщения")] Guid scheduledMessageId,
            [FromBody] UpdateScheduledMessageRequest request,
            CancellationToken ct = default)
        {
            try
            {
                var user = await User.GetCurrentUserAsync(_userService);

                string? encrypted = null;
                if (request.MessageText != null)
                {
                    encrypted = string.IsNullOrWhiteSpace(request.MessageText)
                        ? string.Empty
                        : _encryption.Encrypt(request.MessageText.Trim());
                }

                DateTime? at = request.ScheduledAt?.UtcDateTime;

                var entity = await _scheduledService.UpdateAsync(
                    scheduledMessageId, user.UserId, encrypted, at, ct);

                if (entity == null)
                {
                    return NotFound(ApiResponse.Failure("Запланированное сообщение не найдено", "NOT_FOUND"));
                }

                var plain = request.MessageText?.Trim()
                            ?? (string.IsNullOrEmpty(entity.MessageText)
                                ? null
                                : _encryption.TryDecryptSafe(entity.MessageText));

                var dto = _scheduledService.ToDto(entity, plain,
                    $"{user.FirstName} {user.LastName}".Trim());

                try
                {
                    await _hubContext.Clients.Group($"User_{user.UserId}")
                        .SendAsync("ScheduledMessageUpdated", dto, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SignalR ScheduledMessageUpdated failed");
                }

                return Ok(ApiResponse.Success(dto));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при обновлении запланированного сообщения {Id}", scheduledMessageId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update scheduled {Id} failed", scheduledMessageId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Отменить запланированную отправку.
        /// </summary>
        [HttpDelete("{scheduledMessageId:guid}")]
        [EndpointName("CancelScheduledMessage")]
        [EndpointSummary("Отменить запланированную отправку")]
        [EndpointDescription("Удаляет отложенное сообщение из очереди на отправку.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteCancelAsync(
            [Description("Идентификатор запланированного сообщения")] Guid scheduledMessageId,
            CancellationToken ct = default)
        {
            try
            {
                var user = await User.GetCurrentUserAsync(_userService);

                var ok = await _scheduledService.CancelAsync(scheduledMessageId, user.UserId, ct);
                if (!ok)
                {
                    return NotFound(ApiResponse.Failure("Сообщение не найдено или уже отправлено/отменено", "NOT_FOUND"));
                }

                try
                {
                    await _hubContext.Clients.Group($"User_{user.UserId}")
                        .SendAsync("ScheduledMessageCancelled", new { ScheduledMessageId = scheduledMessageId }, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SignalR ScheduledMessageCancelled failed");
                }

                return Ok(ApiResponse.Success(message: "Запланированное сообщение успешно отменено"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при отмене запланированного сообщения {Id}", scheduledMessageId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cancel scheduled {Id} failed", scheduledMessageId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }
    }
}