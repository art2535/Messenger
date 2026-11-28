using Messenger.Application.DTOs.Responses;
using Messenger.Application.Files;
using Messenger.API.Adapters;
using Messenger.API.Files;
﻿using Asp.Versioning;
using Messenger.Application.DTOs.Attachments;
using Messenger.Application.Interfaces;
using Messenger.API.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для управления вложениями.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/attachments")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [Tags("Attachments")]
    public class AttachmentsController : ControllerBase
    {
        private readonly IAttachmentService _attachmentService;
        private readonly IMessageService _messageService;
        private readonly IChatService _chatService;
        private readonly IUserService _userService;
        private readonly IConfiguration _configuration;
        private readonly IFileStorage _fileStorage;
        private readonly IFileValidationService _fileValidationService;
        private readonly ILogger<AttachmentsController> _logger;

        public AttachmentsController(
            IAttachmentService attachmentService,
            IMessageService messageService,
            IChatService chatService,
            IUserService userService,
            IConfiguration configuration,
            IFileStorage fileStorage,
            IFileValidationService fileValidationService,
            ILogger<AttachmentsController> logger)
        {
            _attachmentService = attachmentService;
            _messageService = messageService;
            _chatService = chatService;
            _userService = userService;
            _configuration = configuration;
            _fileStorage = fileStorage;
            _fileValidationService = fileValidationService;
            _logger = logger;
        }

        /// <summary>
        /// Получение списка вложений по идентификатору сообщения.
        /// </summary>
        [HttpGet("{messageId:guid}")]
        [EndpointName("GetAttachmentsByMessage")]
        [EndpointSummary("Получение списка вложений по идентификатору сообщения")]
        [EndpointDescription("Возвращает все файлы (вложения), прикреплённые к указанному сообщению. Требуется действительный JWT-токен.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAttachmentsByMessageAsync(
            [FromRoute, Description("Уникальный идентификатор сообщения (GUID)")] Guid messageId,
            CancellationToken ct = default)
        {
            try
            {
                var displayFileName = Path.GetFileName(
                    request.FileName.Replace('\\', '/')).Trim();
                if (string.IsNullOrWhiteSpace(displayFileName)
                    || displayFileName.Length > 255
                    || displayFileName.Any(char.IsControl))
                {
                    return BadRequest(ApiResponse.Failure("Некорректное имя файла", "INVALID_REQUEST"));
                }

                var userId = await User.GetCurrentUserIdAsync(_userService, ct);
                var message = await _messageService.GetMessageByIdGlobalAsync(messageId, ct);
                if (message == null)
                    return NotFound(ApiResponse.Failure("Сообщение не найдено", "NOT_FOUND"));

                var chat = await _chatService.GetChatByIdAsync(message.ChatId, userId, ct);
                if (chat == null)
                    return NotFound(ApiResponse.Failure("Чат не найден", "NOT_FOUND"));

                if (!chat.Participants.Any(participant => participant.UserId == userId))
                    return Forbid();

                var attachments = await _attachmentService.GetAttachmentsByMessageIdAsync(messageId, ct);
                return Ok(ApiResponse.Success(attachments));
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ApiResponse.Failure(ex.Message, "NOT_FOUND"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении вложений для сообщения {MessageId}", messageId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Защищённая выдача файла вложения.
        /// </summary>
        [HttpGet("file/{attachmentId:guid}")]
        [EndpointName("DownloadAttachment")]
        [EndpointSummary("Защищённая выдача файла вложения")]
        [EndpointDescription("Возвращает файл только аутентифицированному участнику чата, которому принадлежит сообщение.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DownloadAttachmentAsync(
            [FromRoute, Description("Уникальный идентификатор вложения (GUID)")] Guid attachmentId,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);
                var (attachment, messageId) = await _attachmentService.GetAttachmentWithMessageIdAsync(attachmentId, ct);
                if (attachment == null || !messageId.HasValue)
                    return NotFound(ApiResponse.Failure("Вложение не найдено", "NOT_FOUND"));

                var message = await _messageService.GetMessageByIdGlobalAsync(messageId.Value, ct);
                if (message == null)
                    return NotFound(ApiResponse.Failure("Сообщение не найдено", "NOT_FOUND"));

                var chat = await _chatService.GetChatByIdAsync(message.ChatId, userId, ct);
                if (chat == null)
                    return NotFound(ApiResponse.Failure("Чат не найден", "NOT_FOUND"));

                if (!chat.Participants.Any(participant => participant.UserId == userId))
                    return Forbid();

                if (!Uri.TryCreate(_configuration["URL:API:HTTPS"], UriKind.Absolute, out var apiBaseUri)
                    || !Uri.TryCreate(apiBaseUri, attachment.Url, out var fileUri)
                    || !string.Equals(fileUri.Scheme, apiBaseUri.Scheme, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(fileUri.Host, apiBaseUri.Host, StringComparison.OrdinalIgnoreCase)
                    || fileUri.Port != apiBaseUri.Port)
                {
                    _logger.LogWarning("Вложение {AttachmentId} содержит URL вне API origin", attachmentId);
                    return NotFound(ApiResponse.Failure("Файл вложения не найден", "NOT_FOUND"));
                }

                var normalizedPath = Uri.UnescapeDataString(fileUri.AbsolutePath).Replace('\\', '/');
                const string uploadsPrefix = "/uploads/";
                if (!normalizedPath.StartsWith(uploadsPrefix, StringComparison.OrdinalIgnoreCase))
                    return NotFound(ApiResponse.Failure("Файл вложения не найден", "NOT_FOUND"));

                var storedFileName = normalizedPath[uploadsPrefix.Length..];
                if (string.IsNullOrWhiteSpace(storedFileName)
                    || storedFileName.Contains('/')
                    || storedFileName.Contains('\\')
                    || storedFileName is "." or "..")
                    return NotFound(ApiResponse.Failure("Файл вложения не найден", "NOT_FOUND"));

                var storedFileInfo = await _fileStorage.GetInfoAsync(
                    FileStorageArea.Uploads, storedFileName, ct);
                if (storedFileInfo == null)
                    return NotFound(ApiResponse.Failure("Файл вложения не найден", "NOT_FOUND"));

                ValidatedFile validated;
                await using (var validationStream = await _fileStorage.OpenReadAsync(
                    FileStorageArea.Uploads, storedFileName, ct))
                {
                    if (validationStream == null)
                        return NotFound(ApiResponse.Failure("Файл вложения не найден", "NOT_FOUND"));

                    validated = await _fileValidationService.ValidateAsync(
                        new StoredFileUploadAdapter(storedFileName, storedFileInfo.Length, validationStream),
                        FileUploadPurpose.Attachment,
                        ct);
                }

                var fileStream = await _fileStorage.OpenReadAsync(
                    FileStorageArea.Uploads, storedFileName, ct);
                if (fileStream == null)
                    return NotFound(ApiResponse.Failure("Файл вложения не найден", "NOT_FOUND"));

                var contentType = validated.ContentType;
                var extension = validated.Extension;
                if (string.IsNullOrWhiteSpace(contentType)
                    || !contentType.Contains('/', StringComparison.Ordinal)
                    || contentType.Length > 100)
                {
                    contentType = "application/octet-stream";
                }

                // SVG/HTML/XML and unknown types are always downloaded. The browser only gets
                // inline rendering for known safe raster image types.
                var inlineExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".avif"
                };
                var fileName = new string(Path.GetFileName(attachment.FileName.Replace('\\', '/'))
                    .Where(character => !char.IsControl(character)).ToArray());
                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = storedFileName;
                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = storedFileName;

                Response.Headers["X-Content-Type-Options"] = "nosniff";
                Response.Headers["Cache-Control"] = "private, no-store";
                if (!inlineExtensions.Contains(extension))
                {
                    Response.Headers.ContentDisposition =
                        $"attachment; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
                }

                return File(
                    fileStream,
                    contentType,
                    inlineExtensions.Contains(extension) ? null : fileName,
                    enableRangeProcessing: true);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (UriFormatException ex)
            {
                _logger.LogWarning(ex, "Вложение {AttachmentId} содержит некорректный URL", attachmentId);
                return NotFound(ApiResponse.Failure("Файл вложения не найден", "NOT_FOUND"));
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Вложение {AttachmentId} не прошло проверку содержимого", attachmentId);
                return NotFound(ApiResponse.Failure("Файл вложения не найден", "NOT_FOUND"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выдаче вложения {AttachmentId}", attachmentId);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Добавление нового вложения к сообщению.
        /// </summary>
        [HttpPost("{messageId:guid}")]
        [EndpointName("CreateAttachment")]
        [EndpointSummary("Добавление нового вложения к сообщению")]
        [EndpointDescription("Создаёт запись о новом файле (вложении), прикреплённом к сообщению. " +
            "Требуется передать данные файла в теле запроса. Идентификатор вложения генерируется автоматически. " +
            "Требуется действительный JWT-токен.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostCreateAttachmentAsync(
            [FromRoute, Description("Уникальный идентификатор сообщения (GUID), к которому добавляется вложение")] Guid messageId,
            [FromBody, Description("Данные нового вложения")] CreateAttachmentRequest request,
            CancellationToken ct = default)
        {
            try
            {
                if (request == null
                    || string.IsNullOrWhiteSpace(request.FileName)
                    || request.FileName.Length > 255
                    || request.SizeInBytes <= 0
                    || request.SizeInBytes > 10 * 1024 * 1024
                    || string.IsNullOrWhiteSpace(request.Url)
                    || request.Url.Length > 2048)
                {
                    return BadRequest(ApiResponse.Failure("Некорректные метаданные вложения", "INVALID_REQUEST"));
                }

                var userId = await User.GetCurrentUserIdAsync(_userService, ct);
                var message = await _messageService.GetMessageByIdGlobalAsync(messageId, ct);
                if (message == null)
                    return NotFound(ApiResponse.Failure("Сообщение не найдено", "NOT_FOUND"));

                var chat = await _chatService.GetChatByIdAsync(message.ChatId, userId, ct);
                if (chat == null)
                    return NotFound(ApiResponse.Failure("Чат не найден", "NOT_FOUND"));

                if (message.SenderId != userId
                    || !chat.Participants.Any(participant => participant.UserId == userId))
                    return Forbid();

                if (!Uri.TryCreate(_configuration["URL:API:HTTPS"], UriKind.Absolute, out var apiBaseUri)
                    || !Uri.TryCreate(apiBaseUri, request.Url, out var fileUri)
                    || !string.Equals(fileUri.Scheme, apiBaseUri.Scheme, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(fileUri.Host, apiBaseUri.Host, StringComparison.OrdinalIgnoreCase)
                    || fileUri.Port != apiBaseUri.Port)
                {
                    return BadRequest(ApiResponse.Failure("URL вложения должен указывать на хранилище этого API", "INVALID_REQUEST"));
                }

                var normalizedPath = Uri.UnescapeDataString(fileUri.AbsolutePath).Replace('\\', '/');
                const string uploadsPrefix = "/uploads/";
                if (!normalizedPath.StartsWith(uploadsPrefix, StringComparison.OrdinalIgnoreCase))
                    return BadRequest(ApiResponse.Failure("Вложение должно находиться в каталоге uploads", "INVALID_REQUEST"));

                var storedFileName = normalizedPath[uploadsPrefix.Length..];
                if (string.IsNullOrWhiteSpace(storedFileName)
                    || storedFileName.Contains('/')
                    || storedFileName.Contains('\\')
                    || storedFileName is "." or "..")
                {
                    return BadRequest(ApiResponse.Failure("Некорректный путь вложения", "INVALID_REQUEST"));
                }

                // Новые имена файлов формируются сервером с префиксом ID владельца.
                // Это не позволяет прикрепить к своему сообщению файл другого пользователя,
                // даже если его URL/имя стало известно.
                if (!StoredFileNameGenerator.IsOwnedBy(userId, storedFileName))
                    return Forbid();

                var storedFileInfo = await _fileStorage.GetInfoAsync(
                    FileStorageArea.Uploads, storedFileName, ct);
                if (storedFileInfo == null || storedFileInfo.Length != request.SizeInBytes)
                {
                    return BadRequest(ApiResponse.Failure("Файл вложения не найден или его размер не совпадает с метаданными", "INVALID_REQUEST"));
                }

                await using var storedStream = await _fileStorage.OpenReadAsync(
                    FileStorageArea.Uploads, storedFileName, ct);
                if (storedStream == null)
                    return BadRequest(ApiResponse.Failure("Файл вложения не найден", "INVALID_REQUEST"));

                var validated = await _fileValidationService.ValidateAsync(
                    new StoredFileUploadAdapter(storedFileName, storedFileInfo.Length, storedStream),
                    FileUploadPurpose.Attachment,
                    ct);

                var attachment = new CreateAttachmentDto
                {
                    FileName = displayFileName,
                    FileType = validated.ContentType,
                    SizeInBytes = checked((int)storedFileInfo.Length),
                    Url = fileUri.ToString()
                };

                if (string.IsNullOrWhiteSpace(attachment.FileName))
                    return BadRequest(ApiResponse.Failure("Некорректное имя файла", "INVALID_REQUEST"));

                await _attachmentService.AddAttachmentAsync(messageId, attachment, ct);
                return Ok(ApiResponse.Success(message: "Вложение успешно добавлено"));
            }
            catch (UriFormatException ex)
            {
                _logger.LogWarning(ex, "Некорректный URL файла при создании вложения для сообщения {MessageId}", messageId);
                return BadRequest(ApiResponse.Failure("Некорректный URL вложения", "INVALID_REQUEST"));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при добавлении вложения к сообщению {MessageId}", messageId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }
    }
}