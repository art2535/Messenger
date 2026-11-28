using Messenger.Application.Common;
using Asp.Versioning;
using Messenger.API.Extensions;
using MassTransit;
using Messenger.Application.DTOs.Messages;
using Messenger.Application.DTOs.Chats;
using Messenger.Core.Enums;
using Messenger.Realtime.Hubs;
using Messenger.Application.Interfaces;
using Messenger.Application.Messages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using System.ComponentModel;
using System.Security.Claims;
using Messenger.Realtime.Services;

using Messenger.Application.DTOs.Responses;
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
        private readonly IMessageService _messageService;
        private readonly IConfiguration _configuration;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IChatService _chatService;
        private readonly IUserService _userService;
        private readonly IEncryptionService _encryptionService;
        private readonly IAttachmentService _attachmentService;
        private readonly IAccountSettingsService _accountSettingsService;
        private readonly ILogger<MessagesController> _logger;
        private readonly SendMessageRateLimiterService _sendLimiter;

        public MessagesController(
            IMessageService messageService,
            IConfiguration configuration,
            IHubContext<ChatHub> hubContext,
            IChatService chatService,
            IUserService userService,
            IEncryptionService encryptionService,
            IAttachmentService attachmentService,
            IAccountSettingsService accountSettingsService,
            ILogger<MessagesController> logger,
            SendMessageRateLimiterService sendLimiter)
        {
            _messageService = messageService;
            _configuration = configuration;
            _hubContext = hubContext;
            _chatService = chatService;
            _userService = userService;
            _encryptionService = encryptionService;
            _attachmentService = attachmentService;
            _accountSettingsService = accountSettingsService;
            _logger = logger;
            _sendLimiter = sendLimiter;
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
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);

                if (string.IsNullOrWhiteSpace(query))
                {
                    return Ok(ApiResponse.Success(new List<MessageDto>()
                    ));
                }

                var results = await _messageService.SearchMessagesAsync(chatId, userId, query.Trim(), ct);

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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /messages/{ChatId}/search", chatId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
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
        [RequestSizeLimit(60 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 60 * 1024 * 1024)]
        public async Task<IActionResult> PostSendMessageAsync(
            [FromRoute, Description("Идентификатор чата (GUID)")] Guid chatId,
            [FromForm, Description("Текст сообщения (опционально)")] string? messageText,
            [FromForm, Description("Файлы-вложения (опционально, несколько файлов)")] IFormFile[]? files,
            CancellationToken ct = default)
        {
            var uploadedFilePaths = new List<string>();
            var messagePublished = false;
            try
            {
                var rateKey = User.FindFirst("sub")?.Value
                    ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? HttpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";

                if (!await _sendLimiter.TryAcquireAsync(rateKey, ct))
                {
                    _logger.LogWarning("SendMessage rate limit exceeded for {Key}", rateKey);
                    return StatusCode(StatusCodes.Status429TooManyRequests, ApiResponse.Failure("Слишком много запросов. Подождите немного и попробуйте снова.", "RATE_LIMIT_EXCEEDED"));
                }

                var userId = await User.GetCurrentUserIdAsync(_userService, ct);
                var user = await _userService.GetUserByIdAsync(userId, ct);
                if (user == null)
                {
                    return Unauthorized(ApiResponse.Failure("Пользователь не найден"
                    , "UNAUTHORIZED"));
                }

                const long maxFileSize = 10 * 1024 * 1024;
                const long maxTotalFileSize = 50 * 1024 * 1024;
                const int maxFileCount = 20;
                if (files != null && files.Length > maxFileCount)
                {
                    return BadRequest(ApiResponse.Failure(
                        $"Можно прикрепить не более {maxFileCount} файлов за одно сообщение",
                        "INVALID_REQUEST"));
                }

                long totalFileSize = 0;
                if (files != null)
                {
                    foreach (var file in files)
                    {
                        if (file.Length <= 0)
                        {
                            return BadRequest(ApiResponse.Failure(
                                $"Файл '{SanitizeDisplayFileName(file.FileName)}' пуст",
                                "INVALID_REQUEST"));
                        }

                        if (file.Length > maxFileSize)
                        {
                            return BadRequest(ApiResponse.Failure(
                                $"Файл '{SanitizeDisplayFileName(file.FileName)}' превышает максимальный размер 10 МБ. " +
                                $"Текущий размер: {file.Length / (1024 * 1024):F2} МБ",
                                "INVALID_REQUEST"));
                        }

                        totalFileSize += file.Length;
                        if (totalFileSize > maxTotalFileSize)
                        {
                            return BadRequest(ApiResponse.Failure(
                                "Суммарный размер вложений не должен превышать 50 МБ",
                                "INVALID_REQUEST"));
                        }
                    }
                }

                var chat = await _chatService.GetChatByIdAsync(chatId, userId, ct);
                if (chat == null)
                {
                    return NotFound(ApiResponse.Failure("Чат не найден", "NOT_FOUND"));
                }

                if (!chat.Participants.Any(p => p.UserId == user.UserId))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Failure("Вы не являетесь участником этого чата", "FORBIDDEN"));
                }

                var attachmentsInfo = new List<AttachmentInfo>();
                var attachmentDtos = new List<AttachmentDto>();

                if (files != null && files.Length > 0)
                {
                    var uploadPath = GetUploadDirectory();

                    foreach (var file in files)
                    {
                        var fileName = CreateStoredFileName(file.FileName);
                        var filePath = GetUploadFilePath(uploadPath, fileName);
                        var fileUrl = $"{_configuration["URL:API:HTTPS"]?.TrimEnd('/')}/uploads/{fileName}";
                        var displayFileName = SanitizeDisplayFileName(file.FileName);

                        await using (var stream = new FileStream(
                            filePath,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None,
                            81920,
                            FileOptions.Asynchronous | FileOptions.SequentialScan))
                        {
                            uploadedFilePaths.Add(filePath);
                            await file.CopyToAsync(stream, ct);
                        }

                        var attachmentId = Guid.NewGuid();
                        var fileType = string.IsNullOrWhiteSpace(file.ContentType)
                            ? "application/octet-stream"
                            : file.ContentType;

                        attachmentsInfo.Add(new AttachmentInfo
                        {
                            AttachmentId = attachmentId,
                            FileName = displayFileName,
                            FileType = fileType,
                            SizeInBytes = file.Length,
                            Url = fileUrl
                        });

                        attachmentDtos.Add(new AttachmentDto
                        {
                            AttachmentId = attachmentId,
                            FileName = displayFileName,
                            FileType = fileType,
                            SizeInBytes = (int)file.Length,
                            Url = fileUrl
                        });
                    }
                }

                var messageId = Guid.NewGuid();
                var encryptedText = string.IsNullOrWhiteSpace(messageText)
                    ? null
                    : _encryptionService.Encrypt(messageText.Trim());

                try
                {
                    await _messageService.PublishChatMessageAsync(new ChatMessageSent
                    {
                        MessageId = messageId,
                        ChatId = chatId,
                        SenderId = user.UserId,
                        SenderName = $"{user.FirstName} {user.LastName}".Trim(),
                        MessageText = encryptedText,
                        SentAt = UtcDateTime.AsUtc(UtcDateTime.Now),
                        HasAttachments = attachmentsInfo.Count > 0,
                        Attachments = attachmentsInfo
                    }, ct);
                    messagePublished = true;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    CleanupUploadedFiles(uploadedFilePaths);
                    throw;
                }
                catch (Exception ex)
                {
                    CleanupUploadedFiles(uploadedFilePaths);
                    _logger.LogError(ex, "Ошибка при публикации сообщения в чат {ChatId}", chatId);
                    return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure("Внутренняя ошибка сервера при отправке сообщения", "INTERNAL_ERROR"));
                }

                var decryptedText = string.IsNullOrEmpty(encryptedText)
                    ? null
                    : _encryptionService.TryDecryptSafe(encryptedText);

                var finalMessageDto = new MessageDto
                {
                    MessageId = messageId,
                    ChatId = chatId,
                    SenderId = user.UserId,
                    SenderName = $"{user.FirstName} {user.LastName}".Trim(),
                    MessageText = decryptedText,
                    SentAt = UtcDateTime.AsUtc(UtcDateTime.Now),
                    Status = "Sent",
                    Attachments = attachmentDtos
                };

                try
                {
                    await _hubContext.Clients.Group(chatId.ToString())
                        .SendAsync("ReceiveMessage", finalMessageDto, ct);
                    await _hubContext.Clients.Group(chatId.ToString())
                        .SendAsync("MessageSendingStatus", new
                        {
                            MessageId = messageId,
                            ChatId = chatId,
                            Status = "Sent",
                            Timestamp = DateTimeOffset.UtcNow
                        }, ct);

                    foreach (var participant in chat.Participants)
                    {
                        var userGroup = $"User_{participant.UserId}";
                        await _hubContext.Clients.Group(userGroup)
                            .SendAsync("ReceiveMessage", finalMessageDto, ct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Сообщение {MessageId} сохранено, но SignalR-рассылка не удалась", messageId);
                }

                return Ok(ApiResponse.Success(finalMessageDto));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при отправке сообщения в чат {ChatId}", chatId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (!messagePublished)
                    CleanupUploadedFiles(uploadedFilePaths);
                throw;
            }
            catch (Exception ex)
            {
                if (!messagePublished)
                    CleanupUploadedFiles(uploadedFilePaths);
                _logger.LogError(ex, "Ошибка при отправке сообщения в чат {ChatId}", chatId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
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
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);
                var user = await _userService.GetUserByIdAsync(userId, ct);
                if (user == null)
                {
                    return Unauthorized(ApiResponse.Failure("Пользователь не найден", "UNAUTHORIZED"));
                }

                if (request == null)
                {
                    return BadRequest(ApiResponse.Failure("Тело запроса пустое или неверный JSON", "INVALID_REQUEST"));
                }

                if (request.MessageIds == null || request.MessageIds.Count == 0)
                {
                    return BadRequest(ApiResponse.Failure("Не указаны сообщения для пересылки", "INVALID_REQUEST"));
                }

                if (request.TargetChatIds == null || request.TargetChatIds.Count == 0)
                {
                    return BadRequest(ApiResponse.Failure("Не указаны целевые чаты", "INVALID_REQUEST"));
                }

                if (request.MessageIds.Count > 20)
                {
                    return BadRequest(ApiResponse.Failure("Можно переслать не более 20 сообщений за раз", "INVALID_REQUEST"));
                }

                if (request.TargetChatIds.Count > 5)
                {
                    return BadRequest(ApiResponse.Failure("Можно выбрать не более 5 чатов", "INVALID_REQUEST"));
                }

                request.MessageIds = request.MessageIds.Where(id => id != Guid.Empty).Distinct().ToList();
                request.TargetChatIds = request.TargetChatIds.Where(id => id != Guid.Empty).Distinct().ToList();

                if (request.MessageIds.Count == 0)
                {
                    return BadRequest(ApiResponse.Failure("Некорректные ID сообщений", "INVALID_REQUEST"));
                }

                if (request.TargetChatIds.Count == 0)
                {
                    return BadRequest(ApiResponse.Failure("Некорректные ID чатов", "INVALID_REQUEST"));
                }

                var senderName = $"{user.FirstName} {user.LastName}".Trim();
                if (string.IsNullOrWhiteSpace(senderName))
                    senderName = "Пользователь";

                var sourceMessages = new List<(MessageDto msg, string? plainText, List<AttachmentDto> attachments)>();
                foreach (var mid in request.MessageIds.Distinct())
                {
                    var msg = await _messageService.GetMessageByIdGlobalAsync(mid, ct);
                    if (msg == null)
                    {
                        return NotFound(ApiResponse.Failure($"Сообщение {mid} не найдено", "NOT_FOUND"));
                    }

                    var sourceChat = await _chatService.GetChatByIdAsync(msg.ChatId, userId, ct);
                    if (sourceChat == null || !sourceChat.Participants.Any(p => p.UserId == user.UserId))
                    {
                        return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Failure("Нет доступа к исходному чату", "FORBIDDEN"));
                    }

                    var plain = msg.MessageText;
                    var atts = msg.Attachments ?? new List<AttachmentDto>();
                    sourceMessages.Add((msg, plain, atts));
                }

                var targetChats = new List<ChatDto>();
                foreach (var targetChatId in request.TargetChatIds.Distinct())
                {
                    var chat = await _chatService.GetChatByIdAsync(targetChatId, userId, ct);
                    if (chat == null)
                    {
                        return NotFound(ApiResponse.Failure($"Чат {targetChatId} не найден", "NOT_FOUND"));
                    }

                    if (!chat.Participants.Any(p => p.UserId == user.UserId))
                    {
                        return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Failure($"Нет доступа к целевому чату {targetChatId}", "FORBIDDEN"));
                    }

                    targetChats.Add(chat);
                }

                var uploadPath = GetUploadDirectory();
                var apiBase = (_configuration["URL:API:HTTPS"] ?? "").TrimEnd('/');

                int published = 0;
                var errors = new List<string>();

                foreach (var target in targetChats)
                {
                    foreach (var (srcMsg, plainText, attachments) in sourceMessages)
                    {
                        var copiedFilePaths = new List<string>();
                        var forwardPublished = false;
                        try
                        {
                            var originalSender = string.IsNullOrWhiteSpace(srcMsg.SenderName) ? "Пользователь" : srcMsg.SenderName;
                            if (string.IsNullOrWhiteSpace(originalSender))
                                originalSender = "Пользователь";

                            var body = plainText ?? "";
                            var forwardHeader = "\u200BFORWARD:" + srcMsg.MessageId.ToString() + "|" + originalSender.Replace("|", " ") + "\u200B\n";
                            var contentToEncrypt = forwardHeader + body;
                            var encryptedText = string.IsNullOrWhiteSpace(contentToEncrypt)
                                ? null
                                : _encryptionService.Encrypt(contentToEncrypt.TrimEnd());

                            var newMessageId = Guid.NewGuid();
                            var attachmentsInfo = new List<AttachmentInfo>();

                            foreach (var att in attachments)
                            {
                                var diskName = ExtractUploadFileName(att.Url, att.FileName);
                                var srcPath = GetUploadFilePath(uploadPath, diskName);
                                if (!System.IO.File.Exists(srcPath))
                                {
                                    _logger.LogWarning("Файл вложения не найден: {Path}", srcPath);
                                    continue;
                                }

                                var ext = GetSafeExtension(diskName);
                                var newFileName = $"{Guid.NewGuid():N}{ext}";
                                var destPath = GetUploadFilePath(uploadPath, newFileName);
                                copiedFilePaths.Add(destPath);
                                System.IO.File.Copy(srcPath, destPath, overwrite: false);

                                var newAttId = Guid.NewGuid();
                                var fileUrl = $"{apiBase}/uploads/{newFileName}";
                                var size = att.SizeInBytes > 0 ? att.SizeInBytes : (int)new FileInfo(destPath).Length;

                                attachmentsInfo.Add(new AttachmentInfo
                                {
                                    AttachmentId = newAttId,
                                    FileName = SanitizeDisplayFileName(att.FileName),
                                    FileType = string.IsNullOrWhiteSpace(att.FileType) ? "application/octet-stream" : att.FileType,
                                    SizeInBytes = size,
                                    Url = fileUrl
                                });
                            }

                            await _messageService.PublishChatMessageAsync(new ChatMessageSent
                            {
                                MessageId = newMessageId,
                                ChatId = target.ChatId,
                                SenderId = user.UserId,
                                SenderName = senderName,
                                MessageText = encryptedText,
                                SentAt = UtcDateTime.AsUtc(UtcDateTime.Now),
                                HasAttachments = attachmentsInfo.Count > 0,
                                Attachments = attachmentsInfo
                            }, ct);
                            forwardPublished = true;

                            try
                            {
                                var plainForDto = string.IsNullOrEmpty(encryptedText)
                                    ? null
                                    : _encryptionService.TryDecryptSafe(encryptedText);
                                var forwardDto = new MessageDto
                                {
                                    MessageId = newMessageId,
                                    ChatId = target.ChatId,
                                    SenderId = user.UserId,
                                    SenderName = senderName,
                                    MessageText = plainForDto,
                                    SentAt = UtcDateTime.AsUtc(UtcDateTime.Now),
                                    Status = "Sent",
                                    Attachments = attachmentsInfo.Select(a => new AttachmentDto
                                    {
                                        AttachmentId = a.AttachmentId,
                                        FileName = a.FileName,
                                        FileType = a.FileType,
                                        SizeInBytes = (int)a.SizeInBytes,
                                        Url = a.Url
                                    }).ToList()
                                };
                                var chatIdStr = target.ChatId.ToString();
                                await _hubContext.Clients.Group(chatIdStr)
                                    .SendAsync("ReceiveMessage", forwardDto, ct);
                                await _hubContext.Clients.Group(chatIdStr)
                                    .SendAsync("MessageSendingStatus", new
                                    {
                                        MessageId = newMessageId,
                                        ChatId = target.ChatId,
                                        Status = "Sent",
                                        Timestamp = DateTimeOffset.UtcNow
                                    }, ct);
                                foreach (var participant in target.Participants)
                                {
                                    await _hubContext.Clients.Group($"User_{participant.UserId}")
                                        .SendAsync("ReceiveMessage", forwardDto, ct);
                                }
                            }
                            catch (OperationCanceledException) when (ct.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception signalrEx)
                            {
                                _logger.LogWarning(signalrEx,
                                    "SignalR после forward не удался MessageId={MessageId} ChatId={ChatId}",
                                    newMessageId, target.ChatId);
                            }

                            published++;
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            if (!forwardPublished)
                                CleanupUploadedFiles(copiedFilePaths);
                            throw;
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            if (!forwardPublished)
                                CleanupUploadedFiles(copiedFilePaths);
                            _logger.LogError(ex, "Ошибка пересылки сообщения {MessageId} в чат {ChatId}", srcMsg.MessageId, target.ChatId);
                            errors.Add(ex.Message);
                        }
                    }
                }

                if (published == 0)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(errors.FirstOrDefault() ?? "Не удалось переслать сообщения", "INTERNAL_ERROR"));
                }

                return Ok(ApiResponse.Success(new { published, errors }, meta: new { published, errorCount = errors?.Count ?? 0 }));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при пересылке сообщений");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении POST /messages/forward");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        private static string ExtractUploadFileName(string? url, string? fileName)
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    var path = url;
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                        path = uri.AbsolutePath;

                    var decodedPath = Uri.UnescapeDataString(path.Split('?')[0]).Replace('\\', '/');
                    var name = Path.GetFileName(decodedPath);
                    if (!string.IsNullOrWhiteSpace(name) && name is not "." and not "..")
                        return name;
                }
                catch (UriFormatException)
                {
                    // Некорректный URL не используется как путь к файлу.
                }
            }

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                var normalizedName = fileName.Replace('\\', '/');
                var name = Path.GetFileName(normalizedName);
                if (!string.IsNullOrWhiteSpace(name) && name is not "." and not "..")
                    return name;
            }

            throw new InvalidOperationException("Не удалось определить безопасное имя файла вложения");
        }

        private static string GetUploadDirectory()
        {
            var path = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static string GetUploadFilePath(string uploadDirectory, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)
                || fileName is "." or ".."
                || fileName.Contains('/') || fileName.Contains('\\'))
                throw new InvalidOperationException("Некорректное имя файла вложения");

            var root = Path.GetFullPath(uploadDirectory);
            var fullPath = Path.GetFullPath(Path.Combine(root, fileName));
            var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!fullPath.StartsWith(rootPrefix, comparison))
                throw new InvalidOperationException("Путь вложения выходит за пределы каталога загрузок");

            return fullPath;
        }

        private static string GetSafeExtension(string fileName)
        {
            var normalizedName = fileName.Replace('\\', '/');
            var extension = Path.GetExtension(Path.GetFileName(normalizedName));
            if (extension.Length is < 2 or > 16)
                return ".bin";

            foreach (var character in extension.AsSpan(1))
            {
                var isAsciiLetter = character is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
                var isDigit = character is >= '0' and <= '9';
                if (!isAsciiLetter && !isDigit)
                    return ".bin";
            }

            return extension.ToLowerInvariant();
        }

        private static string CreateStoredFileName(string originalFileName)
            => $"{Guid.NewGuid():N}{GetSafeExtension(originalFileName)}";

        private static string SanitizeDisplayFileName(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "file";

            var normalizedName = fileName.Replace('\\', '/');
            var safeName = Path.GetFileName(normalizedName);
            var characters = safeName.Select(character =>
                char.IsControl(character) ? '_' : character).ToArray();
            safeName = new string(characters).Trim();

            if (safeName.Length == 0)
                return "file";

            return safeName.Length > 255 ? safeName[..255] : safeName;
        }

        private void DeleteStoredAttachmentFiles(IEnumerable<AttachmentDto>? attachments)
        {
            if (attachments == null)
                return;

            var configuredBaseUrl = _configuration["URL:API:HTTPS"];
            if (!Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var apiBaseUri))
            {
                _logger.LogWarning("Не удалось определить API origin; физическое удаление вложений пропущено");
                return;
            }

            foreach (var attachment in attachments)
            {
                if (!Uri.TryCreate(attachment.Url, UriKind.Absolute, out var fileUri))
                    continue;

                if (!string.Equals(fileUri.Scheme, apiBaseUri.Scheme, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(fileUri.Host, apiBaseUri.Host, StringComparison.OrdinalIgnoreCase)
                    || fileUri.Port != apiBaseUri.Port)
                    continue;

                try
                {
                    var normalizedPath = Uri.UnescapeDataString(fileUri.AbsolutePath).Replace('\\', '/');
                    const string uploadsPrefix = "/uploads/";
                    if (!normalizedPath.StartsWith(uploadsPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var fileName = normalizedPath[uploadsPrefix.Length..];
                    if (string.IsNullOrWhiteSpace(fileName)
                        || fileName.Contains('/')
                        || fileName.Contains('\\')
                        || fileName is "." or "..")
                        continue;

                    var filePath = GetUploadFilePath(GetUploadDirectory(), fileName);
                    if (System.IO.File.Exists(filePath))
                        System.IO.File.Delete(filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Не удалось удалить файл вложения {AttachmentId}", attachment.AttachmentId);
                }
            }
        }

        private void CleanupUploadedFiles(IEnumerable<string> filePaths)
        {
            foreach (var filePath in filePaths)
            {
                try
                {
                    if (System.IO.File.Exists(filePath))
                        System.IO.File.Delete(filePath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Не удалось удалить временный файл вложения {FilePath}", filePath);
                }
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
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);

                var chat = await _chatService.GetChatByIdAsync(chatId, userId, ct);
                if (chat == null)
                {
                    return NotFound(ApiResponse.Failure("Чат не найден", "NOT_FOUND"));
                }

                if (!chat.Participants.Any(p => p.UserId == userId))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Failure("Вы не являетесь участником этого чата", "FORBIDDEN"));
                }

                var updated = await _messageService.MarkMessagesAsReadAsync(chatId, userId, ct);

                if (updated > 0)
                {
                    var sendReceipts = true;
                    try
                    {
                        var settings = await _accountSettingsService.GetByUserIdAsync(userId, ct);
                        sendReceipts = settings.ReadReceipts;
                    }
                    catch { }

                    if (sendReceipts)
                    {
                        var readPayload = new
                        {
                            ChatId = chatId,
                            ReaderId = userId,
                            ReadAt = DateTime.UtcNow
                        };

                        await _hubContext.Clients.Group(chatId.ToString())
                            .SendAsync("MessagesRead", readPayload, ct);

                        foreach (var participant in chat.Participants)
                        {
                            await _hubContext.Clients.Group($"User_{participant.UserId}")
                                .SendAsync("MessagesRead", readPayload, ct);
                        }
                    }
                }

                return Ok(ApiResponse.Success(new { updatedCount = updated }, message: "Сообщения обновлены"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при отметке прочтения в чате {ChatId}", chatId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении POST /messages/{ChatId}/read", chatId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
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