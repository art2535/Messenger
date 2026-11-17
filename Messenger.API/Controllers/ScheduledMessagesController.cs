using Asp.Versioning;
using Messenger.API.Responses;
using Messenger.API.Services;
using Messenger.Core.DTOs.Messages;
using Messenger.Core.Hubs;
using Messenger.Core.Interfaces;
using Messenger.Core.Messages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.ComponentModel;

namespace Messenger.API.Controllers;

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

    public ScheduledMessagesController(IScheduledMessageService scheduledService,
        IChatService chatService, IUserService userService, IEncryptionService encryption,
        IConfiguration configuration, IHubContext<ChatHub> hubContext, ILogger<ScheduledMessagesController> logger)
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
    [ProducesResponseType(typeof(ScheduleMessageSuccessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ScheduleAsync([Description("Идентификатор чата")] Guid chatId,
        [FromForm] string? messageText, [FromForm] DateTimeOffset? scheduledAt,
        [FromForm] string? replyToMessageId, [FromForm] IFormFile[]? files,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
            if (error != null) return error;

            if (!scheduledAt.HasValue)
                return BadRequest(new ErrorResponse { Error = "Не указано время отправки (scheduledAt)" });

            var scheduledUtc = scheduledAt.Value.UtcDateTime;
            if (scheduledUtc <= DateTime.UtcNow.AddSeconds(30))
                return BadRequest(new ErrorResponse { Error = "Время отправки должно быть хотя бы на 30 секунд в будущем" });

            var chat = await _chatService.GetChatByIdAsync(chatId, cancellationToken);
            if (chat == null)
                return NotFound(new ErrorResponse { Error = "Чат не найден" });

            if (!chat.ChatParticipants.Any(p => p.UserId == user!.UserId))
                return Forbid();

            const long MAX_FILE_SIZE = 10 * 1024 * 1024;
            if (files != null)
            {
                foreach (var file in files)
                {
                    if (file.Length > MAX_FILE_SIZE)
                        return BadRequest(new ErrorResponse
                        {
                            Error = $"Файл '{file.FileName}' превышает 10 МБ"
                        });
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
                    await file.CopyToAsync(stream, cancellationToken);

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
                return BadRequest(new ErrorResponse { Error = "Пустое сообщение" });

            var encrypted = string.IsNullOrWhiteSpace(messageText)
                ? null
                : _encryption.Encrypt(messageText.Trim());

            var entity = await _scheduledService.CreateAsync(
                chatId,
                user!.UserId,
                encrypted,
                attachmentsInfo.Count > 0,
                attachmentsInfo,
                scheduledUtc,
                replyToMessageId,
                cancellationToken);

            var senderName = $"{user.FirstName} {user.LastName}".Trim();
            var dto = _scheduledService.ToDto(entity, messageText?.Trim(), senderName, chat.Name);

            // SignalR: обновить список запланированных у отправителя
            try
            {
                await _hubContext.Clients.Group($"User_{user.UserId}")
                    .SendAsync("ScheduledMessageCreated", dto, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SignalR ScheduledMessageCreated failed");
            }

            return Ok(new ScheduleMessageSuccessResponse { IsSuccess = true, Data = dto });
        }
        catch (ArgumentException aex)
        {
            return BadRequest(new ErrorResponse { Error = aex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schedule message failed for chat {ChatId}", chatId);
            return StatusCode(500, new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Список всех ожидающих запланированных сообщений текущего пользователя.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ScheduledMessagesListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMineAsync(CancellationToken cancellationToken = default)
    {
        var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
        if (error != null) return error;

        var list = await _scheduledService.GetPendingByUserAsync(user!.UserId, cancellationToken);
        var dtos = list.Select(e =>
        {
            string? plain = null;
            if (!string.IsNullOrEmpty(e.MessageText))
                plain = _encryption.TryDecryptSafe(e.MessageText);
            return _scheduledService.ToDto(e, plain, null, e.Chat?.Name);
        }).ToList();

        return Ok(new ScheduledMessagesListResponse { IsSuccess = true, Data = dtos });
    }

    /// <summary>
    /// Запланированные сообщения в конкретном чате (только свои pending).
    /// </summary>
    [HttpGet("chat/{chatId:guid}")]
    [ProducesResponseType(typeof(ScheduledMessagesListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListByChatAsync(Guid chatId, CancellationToken cancellationToken = default)
    {
        var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
        if (error != null) return error;

        var list = await _scheduledService.GetPendingByChatAsync(chatId, user!.UserId, cancellationToken);
        var dtos = list.Select(e =>
        {
            string? plain = null;
            if (!string.IsNullOrEmpty(e.MessageText))
                plain = _encryption.TryDecryptSafe(e.MessageText);
            return _scheduledService.ToDto(e, plain);
        }).ToList();

        return Ok(new ScheduledMessagesListResponse { IsSuccess = true, Data = dtos });
    }

    /// <summary>
    /// Обновить текст или время запланированного сообщения.
    /// </summary>
    [HttpPut("{scheduledMessageId:guid}")]
    [ProducesResponseType(typeof(ScheduleMessageSuccessResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAsync(
        Guid scheduledMessageId,
        [FromBody] UpdateScheduledMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
            if (error != null) return error;

            string? encrypted = null;
            if (request.MessageText != null)
                encrypted = string.IsNullOrWhiteSpace(request.MessageText)
                    ? string.Empty
                    : _encryption.Encrypt(request.MessageText.Trim());

            DateTime? at = request.ScheduledAt?.UtcDateTime;

            var entity = await _scheduledService.UpdateAsync(
                scheduledMessageId, user!.UserId, encrypted, at, cancellationToken);

            if (entity == null)
                return NotFound(new ErrorResponse { Error = "Запланированное сообщение не найдено" });

            var plain = request.MessageText?.Trim()
                        ?? (string.IsNullOrEmpty(entity.MessageText)
                            ? null
                            : _encryption.TryDecryptSafe(entity.MessageText));

            var dto = _scheduledService.ToDto(entity, plain,
                $"{user.FirstName} {user.LastName}".Trim());

            try
            {
                await _hubContext.Clients.Group($"User_{user.UserId}")
                    .SendAsync("ScheduledMessageUpdated", dto, cancellationToken);
            }
            catch { /* ignore */ }

            return Ok(new ScheduleMessageSuccessResponse { IsSuccess = true, Data = dto });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentException aex)
        {
            return BadRequest(new ErrorResponse { Error = aex.Message });
        }
        catch (InvalidOperationException iox)
        {
            return BadRequest(new ErrorResponse { Error = iox.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update scheduled {Id} failed", scheduledMessageId);
            return StatusCode(500, new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Отменить запланированную отправку.
    /// </summary>
    [HttpDelete("{scheduledMessageId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelAsync(Guid scheduledMessageId, CancellationToken cancellationToken = default)
    {
        try
        {
            var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
            if (error != null) return error;

            var ok = await _scheduledService.CancelAsync(scheduledMessageId, user!.UserId, cancellationToken);
            if (!ok)
                return NotFound(new ErrorResponse { Error = "Сообщение не найдено или уже отправлено/отменено" });

            try
            {
                await _hubContext.Clients.Group($"User_{user.UserId}")
                    .SendAsync("ScheduledMessageCancelled", new { ScheduledMessageId = scheduledMessageId }, cancellationToken);
            }
            catch { /* ignore */ }

            return Ok(new { isSuccess = true });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cancel scheduled {Id} failed", scheduledMessageId);
            return StatusCode(500, new ErrorResponse { Error = ex.Message });
        }
    }
}
