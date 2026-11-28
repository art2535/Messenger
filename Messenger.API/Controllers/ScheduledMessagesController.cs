using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.API.Adapters;
using Messenger.Application.Files;
using Messenger.Application.DTOs.Messages;
using Messenger.Realtime.Hubs;
using Messenger.Application.Interfaces;
using Messenger.Application.Messages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.ComponentModel;

using Messenger.Application.DTOs.Responses;
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
        private readonly IFileStorage _fileStorage;
        private readonly IFileValidationService _fileValidationService;

        public ScheduledMessagesController(
            IScheduledMessageService scheduledService,
            IChatService chatService,
            IUserService userService,
            IEncryptionService encryption,
            IConfiguration configuration,
            IHubContext<ChatHub> hubContext,
            ILogger<ScheduledMessagesController> logger,
            IFileStorage fileStorage,
            IFileValidationService fileValidationService)
        {
            _scheduledService = scheduledService;
            _chatService = chatService;
            _userService = userService;
            _encryption = encryption;
            _configuration = configuration;
            _hubContext = hubContext;
            _logger = logger;
            _fileStorage = fileStorage;
            _fileValidationService = fileValidationService;
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
        [RequestSizeLimit(60 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 60 * 1024 * 1024)]
        public async Task<IActionResult> PostScheduleAsync(
            [Description("Идентификатор чата")] Guid chatId,
            [FromForm] string? messageText,
            [FromForm] DateTimeOffset? scheduledAt,
            [FromForm] string? replyToMessageId,
            [FromForm] IFormFile[]? files,
            CancellationToken ct = default)
        {
            var savedFileNames = new List<string>();
            var scheduledMessageCreated = false;
            try
            {
                var user = await User.GetCurrentUserAsync(_userService, ct);

                if (!scheduledAt.HasValue)
                {
                    return BadRequest(ApiResponse.Failure("Не указано время отправки (scheduledAt)", "INVALID_REQUEST"));
                }

                var scheduledUtc = scheduledAt.Value.UtcDateTime;
                if (scheduledUtc <= DateTime.UtcNow.AddSeconds(30))
                {
                    return BadRequest(ApiResponse.Failure("Время отправки должно быть хотя бы на 30 секунд в будущем", "INVALID_REQUEST"));
                }

                var chat = await _chatService.GetChatByIdAsync(chatId, user.UserId, ct);
                if (chat == null)
                {
                    return NotFound(ApiResponse.Failure("Чат не найден", "NOT_FOUND"));
                }

                if (!chat.Participants.Any(p => p.UserId == user.UserId))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Failure("Доступ запрещён", "FORBIDDEN"));
                }

                var apiBaseUrl = _configuration["URL:API:HTTPS"];
                if (files is { Length: > 0 } && !Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out _))
                    throw new InvalidOperationException("URL:API:HTTPS не задан или имеет некорректный формат");

                const long maxFileSize = 10 * 1024 * 1024;
                const long maxTotalFileSize = 50 * 1024 * 1024;
                const int maxFileCount = 20;
                if (files is { Length: > maxFileCount })
                    return BadRequest(ApiResponse.Failure($"Можно прикрепить не более {maxFileCount} файлов", "INVALID_REQUEST"));

                if (files != null && files.Sum(file => file.Length) > maxTotalFileSize)
                    return BadRequest(ApiResponse.Failure("Суммарный размер вложений не должен превышать 50 МБ", "INVALID_REQUEST"));

                var attachmentsInfo = new List<AttachmentInfo>();
                if (files != null)
                {
                    foreach (var file in files)
                    {
                        var validated = await _fileValidationService.ValidateAsync(
                            new FormFileUploadAdapter(file), FileUploadPurpose.Attachment, ct);
                        var fileName = $"{Guid.NewGuid():N}{validated.Extension}";
                        var fileUrl = $"{apiBaseUrl!.TrimEnd('/')}/uploads/{fileName}";

                        await using var stream = file.OpenReadStream();
                        var storedSize = await _fileStorage.SaveAsync(
                            FileStorageArea.Uploads, fileName, stream, maxFileSize, ct);
                        savedFileNames.Add(fileName);

                        if (storedSize != validated.Length)
                            throw new InvalidDataException("Размер загруженного файла не совпадает с заявленным размером");

                        attachmentsInfo.Add(new AttachmentInfo
                        {
                            AttachmentId = Guid.NewGuid(),
                            FileName = validated.FileName,
                            FileType = validated.ContentType,
                            SizeInBytes = storedSize,
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

                var dto = await _scheduledService.CreateAsync(chatId, user.UserId, encrypted,
                    attachmentsInfo.Count > 0, attachmentsInfo, scheduledUtc, replyToMessageId, ct);
                scheduledMessageCreated = true;


                try
                {
                    await _hubContext.Clients.Group($"User_{user.UserId}")
                        .SendAsync("ScheduledMessageCreated", dto, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SignalR ScheduledMessageCreated failed");
                }

                return Ok(ApiResponse.Success(dto));
            }
            catch (UnauthorizedAccessException ex)
            {
                if (!scheduledMessageCreated)
                    await CleanupUploadedFilesAsync(savedFileNames);
                _logger.LogWarning(ex, "Ошибка авторизации при планировании сообщения");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (ArgumentException ex)
            {
                if (!scheduledMessageCreated)
                    await CleanupUploadedFilesAsync(savedFileNames);
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (InvalidDataException ex)
            {
                if (!scheduledMessageCreated)
                    await CleanupUploadedFilesAsync(savedFileNames);
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (!scheduledMessageCreated)
                    await CleanupUploadedFilesAsync(savedFileNames);
                throw;
            }
            catch (Exception ex)
            {
                if (!scheduledMessageCreated)
                    await CleanupUploadedFilesAsync(savedFileNames);
                _logger.LogError(ex, "Schedule message failed for chat {ChatId}", chatId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        private async Task CleanupUploadedFilesAsync(IEnumerable<string> fileNames)
        {
            foreach (var fileName in fileNames)
            {
                try
                {
                    await _fileStorage.DeleteAsync(
                        FileStorageArea.Uploads, fileName, CancellationToken.None);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Не удалось удалить временный файл запланированного сообщения {FileName}", fileName);
                }
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
                var user = await User.GetCurrentUserAsync(_userService, ct);

                var dtos = await _scheduledService.GetPendingByUserAsync(user.UserId, ct);

                return Ok(ApiResponse.Success(dtos));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении списка запланированных сообщений");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
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
                var user = await User.GetCurrentUserAsync(_userService, ct);

                var dtos = await _scheduledService.GetPendingByChatAsync(chatId, user.UserId, ct);

                return Ok(ApiResponse.Success(dtos));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении запланированных сообщений чата {ChatId}", chatId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
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
                var user = await User.GetCurrentUserAsync(_userService, ct);

                string? encrypted = null;
                if (request.MessageText != null)
                {
                    encrypted = string.IsNullOrWhiteSpace(request.MessageText)
                        ? string.Empty
                        : _encryption.Encrypt(request.MessageText.Trim());
                }

                DateTime? at = request.ScheduledAt?.UtcDateTime;

                var dto = await _scheduledService.UpdateAsync(
                    scheduledMessageId, user.UserId, encrypted, at, ct);

                if (dto == null)
                {
                    return NotFound(ApiResponse.Failure("Запланированное сообщение не найдено", "NOT_FOUND"));
                }

                

                try
                {
                    await _hubContext.Clients.Group($"User_{user.UserId}")
                        .SendAsync("ScheduledMessageUpdated", dto, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
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
                var user = await User.GetCurrentUserAsync(_userService, ct);

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
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cancel scheduled {Id} failed", scheduledMessageId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }
    }
}