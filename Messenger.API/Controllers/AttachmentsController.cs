using Messenger.Application.DTOs.Responses;
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
        private readonly ILogger<AttachmentsController> _logger;

        public AttachmentsController(
            IAttachmentService attachmentService,
            IMessageService messageService,
            IChatService chatService,
            IUserService userService,
            IConfiguration configuration,
            ILogger<AttachmentsController> logger)
        {
            _attachmentService = attachmentService;
            _messageService = messageService;
            _chatService = chatService;
            _userService = userService;
            _configuration = configuration;
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
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var message = await _messageService.GetMessageByIdGlobalAsync(messageId, ct);
                if (message == null)
                    return NotFound(ApiResponse.Failure("Сообщение не найдено", "NOT_FOUND"));

                var chat = await _chatService.GetChatByIdAsync(message.ChatId, ct);
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении вложений для сообщения {MessageId}", messageId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
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
                    || string.IsNullOrWhiteSpace(request.FileType)
                    || request.FileType.Length > 100
                    || request.SizeInBytes <= 0
                    || request.SizeInBytes > 10 * 1024 * 1024
                    || string.IsNullOrWhiteSpace(request.Url)
                    || request.Url.Length > 2048)
                {
                    return BadRequest(ApiResponse.Failure("Некорректные метаданные вложения", "INVALID_REQUEST"));
                }

                var userId = await User.GetCurrentUserIdAsync(_userService);
                var message = await _messageService.GetMessageByIdGlobalAsync(messageId, ct);
                if (message == null)
                    return NotFound(ApiResponse.Failure("Сообщение не найдено", "NOT_FOUND"));

                var chat = await _chatService.GetChatByIdAsync(message.ChatId, ct);
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

                var uploadDirectory = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads"));
                var storedFilePath = Path.GetFullPath(Path.Combine(uploadDirectory, storedFileName));
                var uploadPrefix = uploadDirectory.EndsWith(Path.DirectorySeparatorChar)
                    ? uploadDirectory
                    : uploadDirectory + Path.DirectorySeparatorChar;
                var pathComparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                if (!storedFilePath.StartsWith(uploadPrefix, pathComparison)
                    || !System.IO.File.Exists(storedFilePath)
                    || new FileInfo(storedFilePath).Length != request.SizeInBytes)
                {
                    return BadRequest(ApiResponse.Failure("Файл вложения не найден или его размер не совпадает с метаданными", "INVALID_REQUEST"));
                }

                var attachment = new CreateAttachmentDto
                {
                    FileName = Path.GetFileName(request.FileName.Replace('\\', '/')),
                    FileType = request.FileType,
                    SizeInBytes = request.SizeInBytes,
                    Url = fileUri.ToString()
                };

                if (string.IsNullOrWhiteSpace(attachment.FileName))
                    return BadRequest(ApiResponse.Failure("Некорректное имя файла", "INVALID_REQUEST"));

                await _attachmentService.AddAttachmentAsync(messageId, attachment, ct);
                return Ok(ApiResponse.Success(message: "Вложение успешно добавлено"));
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
                _logger.LogError(ex, "Ошибка при добавлении вложения к сообщению {MessageId}", messageId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }
    }
}