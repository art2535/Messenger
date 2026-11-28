using Messenger.Application.Common;
using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.API.Adapters;
using Messenger.Application.Files;
using MassTransit;
using Messenger.Application.DTOs.Messages;
using Messenger.Application.DTOs.Chats;
using Messenger.Core.Enums;
using Messenger.Realtime.Hubs;
using Messenger.Application.Interfaces;
using Messenger.Application.Messages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.ComponentModel;
using System.Security.Claims;

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
        private readonly IRateLimitService _rateLimitService;
        private readonly IFileStorage _fileStorage;
        private readonly IFileValidationService _fileValidationService;

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
            IRateLimitService rateLimitService,
            IFileStorage fileStorage,
            IFileValidationService fileValidationService)
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
            _rateLimitService = rateLimitService;
            _fileStorage = fileStorage;
            _fileValidationService = fileValidationService;
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
            var uploadedFileNames = new List<string>();
            var messagePublished = false;
            try
            {
                var rateKey = User.FindFirst("sub")?.Value
                    ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? HttpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";

                if (!await _rateLimitService.TryAcquireAsync(
                    "http:send-message:" + rateKey,
                    30,
                    TimeSpan.FromMinutes(1),
                    ct))
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

                var apiBaseUrl = _configuration["URL:API:HTTPS"];
                if (files is { Length: > 0 } && !Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out _))
                    throw new InvalidOperationException("URL:API:HTTPS не задан или имеет некорректный формат");

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
                    foreach (var file in files)
                    {
                        var validated = await _fileValidationService.ValidateAsync(
                            new FormFileUploadAdapter(file), FileUploadPurpose.Attachment, ct);
                        var fileName = CreateStoredFileName(userId, validated.Extension);
                        var fileUrl = $"{apiBaseUrl!.TrimEnd('/')}/uploads/{fileName}";
                        var displayFileName = validated.FileName;

                        await using var stream = file.OpenReadStream();
                        var storedSize = await _fileStorage.SaveAsync(
                            FileStorageArea.Uploads, fileName, stream, maxFileSize, ct);
                        uploadedFileNames.Add(fileName);

                        if (storedSize != validated.Length)
                            throw new InvalidDataException("Размер загруженного файла не совпадает с заявленным размером");

                        var attachmentId = Guid.NewGuid();
                        var fileType = validated.ContentType;

                        attachmentsInfo.Add(new AttachmentInfo
                        {
                            AttachmentId = attachmentId,
                            FileName = displayFileName,
                            FileType = fileType,
                            SizeInBytes = storedSize,
                            Url = fileUrl
                        });

                        attachmentDtos.Add(new AttachmentDto
                        {
                            AttachmentId = attachmentId,
                            FileName = displayFileName,
                            FileType = fileType,
                            SizeInBytes = checked((int)storedSize),
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
                    await CleanupUploadedFilesAsync(uploadedFileNames);
                    throw;
                }
                catch (Exception ex)
                {
                    await CleanupUploadedFilesAsync(uploadedFileNames);
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
            catch (ArgumentException ex)
            {
                await CleanupUploadedFilesAsync(uploadedFileNames);
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (InvalidDataException ex)
            {
                await CleanupUploadedFilesAsync(uploadedFileNames);
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (!messagePublished)
                    await CleanupUploadedFilesAsync(uploadedFileNames);
                throw;
            }
            catch (Exception ex)
            {
                if (!messagePublished)
                    await CleanupUploadedFilesAsync(uploadedFileNames);
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

                var apiBaseUrl = _configuration["URL:API:HTTPS"];
                if (sourceMessages.Any(source => source.attachments.Count > 0)
                    && !Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out _))
                    throw new InvalidOperationException("URL:API:HTTPS не задан или имеет некорректный формат");
                var apiBase = apiBaseUrl?.TrimEnd('/') ?? "";

                int published = 0;
                var errors = new List<string>();

                foreach (var target in targetChats)
                {
                    foreach (var (srcMsg, plainText, attachments) in sourceMessages)
                    {
                        var copiedFileNames = new List<string>();
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
                                var sourceInfo = await _fileStorage.GetInfoAsync(
                                    FileStorageArea.Uploads, diskName, ct);
                                if (sourceInfo == null)
                                {
                                    _logger.LogWarning("Файл вложения не найден: {FileName}", diskName);
                                    continue;
                                }

                                ValidatedFile validatedSource;
                                await using (var validationStream = await _fileStorage.OpenReadAsync(
                                    FileStorageArea.Uploads, diskName, ct))
                                {
                                    if (validationStream == null)
                                        continue;

                                    validatedSource = await _fileValidationService.ValidateAsync(
                                        new StoredFileUploadAdapter(diskName, sourceInfo.Length, validationStream),
                                        FileUploadPurpose.Attachment,
                                        ct);
                                }

                                await using var sourceStream = await _fileStorage.OpenReadAsync(
                                    FileStorageArea.Uploads, diskName, ct);
                                if (sourceStream == null)
                                    continue;

                                var newFileName = CreateStoredFileName(userId, validatedSource.Extension);
                                var copiedSize = await _fileStorage.SaveAsync(
                                    FileStorageArea.Uploads, newFileName, sourceStream, 10 * 1024 * 1024, ct);
                                copiedFileNames.Add(newFileName);

                                var newAttId = Guid.NewGuid();
                                var fileUrl = $"{apiBase}/uploads/{newFileName}";
                                var size = checked((int)copiedSize);

                                attachmentsInfo.Add(new AttachmentInfo
                                {
                                    AttachmentId = newAttId,
                                    FileName = SanitizeDisplayFileName(att.FileName),
                                    FileType = validatedSource.ContentType,
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
                                await CleanupUploadedFilesAsync(copiedFileNames);
                            throw;
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            if (!forwardPublished)
                                await CleanupUploadedFilesAsync(copiedFileNames);
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

        private static string CreateStoredFileName(Guid ownerId, string validatedExtension)
            => $"{ownerId:N}_{Guid.NewGuid():N}{validatedExtension}";

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

        private async Task DeleteStoredAttachmentFilesAsync(IEnumerable<AttachmentDto>? attachments)
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

                    await _fileStorage.DeleteAsync(
                        FileStorageArea.Uploads, fileName, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Не удалось удалить файл вложения {AttachmentId}", attachment.AttachmentId);
                }
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
                    _logger.LogWarning(ex, "Не удалось удалить временный файл вложения {FileName}", fileName);
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
            [FromRoute, Description("Идентификатор чата (GUID)")] Guid chatId,
            [FromQuery, Description("Загрузить сообщения старше этого SequenceNumber")] long? beforeSequence = null,
            [FromQuery, Description("Размер страницы (1–200, по умолчанию 50)")] int limit = 50,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);

                var (messages, hasMore) = await _messageService.GetMessagesPagedAsync(
                    chatId, userId, beforeSequence, limit, ct);

                var dtos = messages.ToList();

                return Ok(ApiResponse.Success(dtos, meta: new
                {
                    hasMore,
                    nextBeforeSequence = dtos.Count > 0 ? dtos.Min(d => d.SequenceNumber) : (long?)null
                }));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении сообщений чата {ChatId}", chatId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /messages/{ChatId}", chatId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
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
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);

                var participants = await _chatService.GetChatParticipantsAsync(chatId, userId, ct);
                if (!participants.Any(p => p.UserId == userId))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Failure("Вы не являетесь участником этого чата", "FORBIDDEN"));
                }

                var chat = await _chatService.GetChatByIdAsync(chatId, userId, ct);
                if (chat == null)
                {
                    return NotFound(ApiResponse.Failure("Чат не найден", "NOT_FOUND"));
                }

                var normalizedFormat = (format ?? "txt").Trim().ToLowerInvariant();
                if (normalizedFormat is not ("txt" or "json" or "html" or "csv"))
                {
                    return BadRequest(ApiResponse.Failure("Поддерживаемые форматы: txt, json, html, csv", "INVALID_REQUEST"));
                }

                var result = await _messageService.ExportChatAsync(chatId, userId, normalizedFormat, chat.Name, ct);

                return File(result.Content, result.ContentType, result.FileName);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при экспорте чата {ChatId}", chatId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /messages/{ChatId}/export", chatId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
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
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);

                var message = await _messageService.GetMessageByIdAsync(request.ChatId, messageId, userId, ct);
                if (message == null)
                {
                    return NotFound(ApiResponse.Failure("Сообщение не найдено", "NOT_FOUND"));
                }

                var chat = await _chatService.GetChatByIdAsync(request.ChatId, userId, ct);
                if (chat == null || !chat.Participants.Any(p => p.UserId == userId))
                    return Forbid();

                if (message.SenderId != userId)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Failure("Редактировать можно только своё сообщение", "FORBIDDEN"));
                }

                var encryptedContent = _encryptionService.Encrypt(request.MessageText);
                await _messageService.UpdateMessageContentAsync(messageId, userId, encryptedContent, ct);

                var updatedDto = new MessageDto
                {
                    MessageId = message.MessageId,
                    ChatId = request.ChatId,
                    SenderId = message.SenderId,
                    MessageText = request.MessageText.Trim(),
                    SentAt = message.SentAt,
                    Status = message.Status,
                    SequenceNumber = message.SequenceNumber
                };

                await _hubContext.Clients.Group(request.ChatId.ToString())
                    .SendAsync("ReceiveMessage", updatedDto, ct);

                return Ok(ApiResponse.Success(message: "Сообщение обновлено"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при редактировании сообщения {MessageId}", messageId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении PUT /messages/{MessageId}", messageId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
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
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);

                var message = await _messageService.GetMessageByIdAsync(chatId, messageId, userId, ct);
                if (message == null)
                {
                    return NotFound(ApiResponse.Failure("Сообщение не найдено", "NOT_FOUND"));
                }

                var chat = await _chatService.GetChatByIdAsync(chatId, userId, ct);
                if (chat == null || !chat.Participants.Any(p => p.UserId == userId))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Failure("Вы не являетесь участником этого чата", "FORBIDDEN"));
                }

                if (message.SenderId != userId)
                    return Forbid();

                await _messageService.DeleteMessageAsync(messageId, userId, ct);
                await DeleteStoredAttachmentFilesAsync(message.Attachments);

                var deletedPayload = new
                {
                    MessageId = messageId,
                    ChatId = chatId
                };

                await _hubContext.Clients.Group(chatId.ToString())
                    .SendAsync("MessageDeleted", deletedPayload, ct);

                foreach (var participant in chat.Participants)
                {
                    await _hubContext.Clients.Group($"User_{participant.UserId}")
                        .SendAsync("MessageDeleted", deletedPayload, ct);
                }

                return Ok(ApiResponse.Success(message: "Сообщение удалено"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при удалении сообщения {MessageId}", messageId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении DELETE /messages/{MessageId}", messageId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
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
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);

                if (request == null || request.MessageIds == null || request.MessageIds.Count == 0)
                {
                    return BadRequest(ApiResponse.Failure("Не указаны сообщения для удаления", "INVALID_REQUEST"));
                }

                if (request.MessageIds.Count > 100)
                {
                    return BadRequest(ApiResponse.Failure("За один раз можно удалить не более 100 сообщений", "INVALID_REQUEST"));
                }

                var chatId = request.ChatId;
                var chat = await _chatService.GetChatByIdAsync(chatId, userId, ct);
                if (chat == null)
                    return NotFound(ApiResponse.Failure("Чат не найден", "NOT_FOUND"));

                if (!chat.Participants.Any(participant => participant.UserId == userId))
                    return Forbid();

                var allowed = new List<Guid>();
                var attachmentsToDelete = new List<AttachmentDto>();

                foreach (var mid in request.MessageIds.Distinct())
                {
                    var message = await _messageService.GetMessageByIdAsync(chatId, mid, userId, ct);
                    if (message == null)
                        continue;

                    if (message.SenderId != userId)
                        return Forbid();

                    allowed.Add(mid);
                    if (message.Attachments != null)
                        attachmentsToDelete.AddRange(message.Attachments);
                }

                if (allowed.Count == 0)
                {
                    return NotFound(ApiResponse.Failure("Сообщения не найдены", "NOT_FOUND"));
                }

                var deleted = await _messageService.DeleteMessagesAsync(allowed, chatId, userId, ct);
                if (deleted > 0)
                    await DeleteStoredAttachmentFilesAsync(attachmentsToDelete);

                var bulkChat = await _chatService.GetChatByIdAsync(chatId, userId, ct);
                var participants = bulkChat?.Participants?.ToList()
                    ?? new List<ChatParticipantDto>();

                foreach (var mid in allowed)
                {
                    var payload = new
                    {
                        MessageId = mid,
                        ChatId = chatId
                    };

                    await _hubContext.Clients.Group(chatId.ToString())
                        .SendAsync("MessageDeleted", payload, ct);

                    foreach (var participant in participants)
                    {
                        await _hubContext.Clients.Group($"User_{participant.UserId}")
                            .SendAsync("MessageDeleted", payload, ct);
                    }
                }

                return Ok(ApiResponse.Success(new { deletedCount = deleted, messageIds = allowed }, message: "Сообщения удалены"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при массовом удалении сообщений");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении POST /messages/bulk-delete");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        private static string GetMimeType(string fileName)
        {
            var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
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
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);

                MessageDto? message;
                try
                {
                    message = await _messageService.PinMessageAsync(messageId, userId, ct);
                }
                catch (UnauthorizedAccessException)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Failure("Недостаточно прав для закрепления сообщения", "FORBIDDEN"));
                }

                if (message == null)
                {
                    return NotFound(ApiResponse.Failure("Сообщение не найдено", "NOT_FOUND"));
                }

                var text = message.MessageText;

                var payload = new
                {
                    MessageId = message.MessageId,
                    ChatId = message.ChatId,
                    IsPinned = true,
                    PinnedAt = message.PinnedAt.HasValue ? UtcDateTime.AsUtc(message.PinnedAt.Value) : (DateTime?)null,
                    Text = (text ?? string.Empty).Length > 200 ? (text ?? string.Empty)[..200] : (text ?? string.Empty),
                    PinnedBy = userId
                };

                await _hubContext.Clients.Group(message.ChatId.ToString())
                    .SendAsync("MessagePinned", payload, ct);

                var chat = await _chatService.GetChatByIdAsync(message.ChatId, userId, ct);
                if (chat?.Participants != null)
                {
                    foreach (var participant in chat.Participants)
                    {
                        await _hubContext.Clients.Group($"User_{participant.UserId}")
                            .SendAsync("MessagePinned", payload, ct);
                    }
                }

                return Ok(ApiResponse.Success(payload, "Сообщение закреплено"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при закреплении сообщения {MessageId}", messageId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении POST /messages/{MessageId}/pin", messageId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
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
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);

                MessageDto? message;
                try
                {
                    message = await _messageService.UnpinMessageAsync(messageId, userId, ct);
                }
                catch (UnauthorizedAccessException)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Failure("Недостаточно прав для открепления сообщения", "FORBIDDEN"));
                }

                if (message == null)
                {
                    return NotFound(ApiResponse.Failure("Сообщение не найдено", "NOT_FOUND"));
                }

                var payload = new
                {
                    MessageId = message.MessageId,
                    ChatId = message.ChatId,
                    IsPinned = false,
                    PinnedAt = (DateTime?)null,
                    Text = (string?)null,
                    PinnedBy = userId
                };

                await _hubContext.Clients.Group(message.ChatId.ToString())
                    .SendAsync("MessagePinned", payload, ct);

                var chat = await _chatService.GetChatByIdAsync(message.ChatId, userId, ct);
                if (chat?.Participants != null)
                {
                    foreach (var participant in chat.Participants)
                    {
                        await _hubContext.Clients.Group($"User_{participant.UserId}")
                            .SendAsync("MessagePinned", payload, ct);
                    }
                }

                return Ok(ApiResponse.Success(payload, "Сообщение откреплено"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при откреплении сообщения {MessageId}", messageId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении DELETE /messages/{MessageId}/pin", messageId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
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
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);

                var chat = await _chatService.GetChatByIdAsync(chatId, userId, ct);
                if (chat == null || !chat.Participants.Any(p => p.UserId == userId))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Failure("Вы не являетесь участником этого чата", "FORBIDDEN"));
                }

                var messages = await _messageService.GetPinnedMessagesAsync(chatId, userId, ct);
                var data = messages.Select(m =>
                {
                    var decrypted = m.MessageText;
                    return new
                    {
                        MessageId = m.MessageId,
                        ChatId = m.ChatId,
                        Text = (decrypted ?? string.Empty).Length > 200
                            ? (decrypted ?? string.Empty)[..200]
                            : (decrypted ?? string.Empty),
                        PinnedAt = m.PinnedAt.HasValue ? UtcDateTime.AsUtc(m.PinnedAt.Value) : (DateTime?)null,
                        SequenceNumber = m.SequenceNumber,
                        SenderId = m.SenderId,
                        SenderName = m.SenderName
                    };
                }).ToList();

                return Ok(ApiResponse.Success(data));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении закреплённых сообщений чата {ChatId}", chatId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /messages/{ChatId}/pinned", chatId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }
    }
}
