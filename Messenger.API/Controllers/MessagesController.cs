using Asp.Versioning;
using MassTransit;
using Messenger.API.Responses;
using Messenger.API.Services;
using Messenger.Core.DTOs.Messages;
using Messenger.Core.Hubs;
using Messenger.Core.Interfaces;
using Messenger.Core.Messages;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using System.ComponentModel;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для управления сообщениями
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
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
        private readonly ILogger<MessagesController> _logger;

        public MessagesController(IMessageService messageService, IConfiguration configuration,
            IHubContext<ChatHub> hubContext, IChatService chatService, IUserService userService,
            IEncryptionService encryptionService, IAttachmentService attachmentService,
            ILogger<MessagesController> logger)
        {
            _messageService = messageService;
            _configuration = configuration;
            _hubContext = hubContext;
            _chatService = chatService;
            _userService = userService;
            _encryptionService = encryptionService;
            _attachmentService = attachmentService;
            _logger = logger;
        }

        /// <summary>
        /// Поиск сообщений в чате
        /// </summary>
        [HttpGet("{chatId:guid}/search")]
        [EndpointName("SearchMessages")]
        [EndpointSummary("Поиск сообщений в чате")]
        [EndpointDescription("Возвращает сообщения, соответствующие критерию поиска в указанном чате.")]
        [ProducesResponseType(typeof(MessageSearchResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SearchMessages([Description("Идентификатор чата (GUID)")] Guid chatId,
            [FromQuery, Description("Критерий поиска")] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Ok(new MessageSearchResponse
                {
                    IsSuccess = true,
                    Data = new List<MessageDto>()
                });
            }

            try
            {
                var results = await _messageService.SearchMessagesAsync(chatId, query.Trim());
                return Ok(new MessageSearchResponse
                {
                    IsSuccess = true,
                    Data = results
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Отправить сообщение в чат
        /// </summary>
        [HttpPost("{chatId:guid}")]
        [EnableRateLimiting("send-message")]
        [EndpointName("SendMessage")]
        [EndpointSummary("Отправить сообщение в чат")]
        [EndpointDescription("Отправляет текстовое сообщение и/или файлы (вложения) в указанный чат. " +
            "Сообщение сохраняется асинхронно через consumer и рассылается через SignalR.")]
        [Consumes("multipart/form-data", "application/json")]
        [ProducesResponseType(typeof(SendMessageSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SendMessageAsync(
            [Description("Идентификатор чата (GUID)")] Guid chatId,
            [FromForm, Description("Текст сообщения (опционально)")] string? messageText,
            [FromForm, Description("Файлы-вложения (опционально, несколько файлов)")] IFormFile[]? files,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                    return error;

                const long MAX_FILE_SIZE = 10 * 1024 * 1024;
                if (files != null && files.Length > 0)
                {
                    foreach (var file in files)
                    {
                        if (file.Length > MAX_FILE_SIZE)
                        {
                            return BadRequest(new ErrorResponse
                            {
                                IsSuccess = false,
                                Error = $"Файл '{file.FileName}' превышает максимальный размер 10 МБ. " +
                                        $"Текущий размер: {file.Length / (1024 * 1024):F2} МБ"
                            });
                        }
                    }
                }

                var chat = await _chatService.GetChatByIdAsync(chatId, cancellationToken);
                if (chat == null)
                {
                    return NotFound(new ErrorResponse { Error = "Чат не найден" });
                }

                if (!chat.ChatParticipants.Any(p => p.UserId == user!.UserId))
                {
                    return Forbid();
                }

                if (chat.Type == "private")
                {
                    var recipientId = chat.ChatParticipants.FirstOrDefault(p => p.UserId != user!.UserId)?.UserId;
                    if (recipientId != null)
                    {
                        bool blockedByRecipient = await _userService.IsBlockedByAsync(recipientId.Value, user!.UserId, cancellationToken);
                        bool blockedByMe = await _userService.IsBlockedByAsync(user!.UserId, recipientId.Value, cancellationToken);

                        if (blockedByRecipient || blockedByMe)
                        {
                            return BadRequest(new ErrorResponse
                            {
                                IsSuccess = false,
                                Error = blockedByMe
                                    ? "Вы не можете отправить сообщение, так как заблокировали этого пользователя."
                                    : "Вы не можете отправлять сообщения этому пользователю — вы в его чёрном списке."
                            });
                        }
                    }
                }

                var contentToSave = string.IsNullOrWhiteSpace(messageText)
                    ? null
                    : _encryptionService.Encrypt(messageText.Trim());

                var attachmentsInfo = new List<AttachmentInfo>();
                var attachmentDtos = new List<AttachmentDto>();

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

                        var attachmentId = Guid.NewGuid();

                        attachmentsInfo.Add(new AttachmentInfo
                        {
                            AttachmentId = attachmentId,
                            FileName = file.FileName,
                            FileType = file.ContentType ?? "application/octet-stream",
                            SizeInBytes = file.Length,
                            Url = fileUrl
                        });

                        attachmentDtos.Add(new AttachmentDto
                        {
                            AttachmentId = attachmentId,
                            FileName = file.FileName,
                            FileType = file.ContentType ?? "application/octet-stream",
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
                        SenderId = user!.UserId,
                        SenderName = $"{user.FirstName} {user.LastName}".Trim(),
                        MessageText = encryptedText,
                        SentAt = DateTime.UtcNow,
                        HasAttachments = attachmentsInfo.Count > 0,
                        Attachments = attachmentsInfo
                    }, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при публикации сообщения в чат {ChatId}", chatId);
                    return StatusCode(500, new ErrorResponse
                    {
                        IsSuccess = false,
                        Error = "Внутренняя ошибка сервера при отправке сообщения"
                    });
                }

                try
                {
                    var decryptedText = string.IsNullOrEmpty(encryptedText)
                        ? null
                        : _encryptionService.TryDecryptSafe(encryptedText);

                    var finalMessageDto = new MessageDto
                    {
                        MessageId = messageId,
                        ChatId = chatId,
                        SenderId = user!.UserId,
                        SenderName = $"{user.FirstName} {user.LastName}".Trim(),
                        MessageText = decryptedText,
                        SentAt = DateTime.UtcNow,
                        Status = "Sent",
                        Attachments = attachmentDtos
                    };

                    await _hubContext.Clients.Group(chatId.ToString())
                        .SendAsync("ReceiveMessage", finalMessageDto, cancellationToken);
                    await _hubContext.Clients.Group(chatId.ToString())
                        .SendAsync("MessageSendingStatus", new
                        {
                            MessageId = messageId,
                            ChatId = chatId,
                            Status = "Sent",
                            Timestamp = DateTimeOffset.UtcNow
                        }, cancellationToken);

                    foreach (var participant in chat.ChatParticipants)
                    {
                        var userGroup = $"User_{participant.UserId}";
                        await _hubContext.Clients.Group(userGroup)
                            .SendAsync("ReceiveMessage", finalMessageDto, cancellationToken);
                    }

                    return Ok(new SendMessageSuccessResponse
                    {
                        IsSuccess = true,
                        Data = finalMessageDto
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Сообщение {MessageId} сохранено, но SignalR/ответ не удались", messageId);
                    return Ok(new SendMessageSuccessResponse
                    {
                        IsSuccess = true,
                        Data = new MessageDto
                        {
                            MessageId = messageId,
                            ChatId = chatId,
                            SenderId = user!.UserId,
                            SenderName = $"{user.FirstName} {user.LastName}".Trim(),
                            MessageText = string.IsNullOrEmpty(encryptedText) ? null : _encryptionService.TryDecryptSafe(encryptedText),
                            SentAt = DateTime.UtcNow,
                            Status = "Sent",
                            Attachments = attachmentDtos
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отправке сообщения в чат {ChatId}", chatId);
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Переслать одно или несколько сообщений в другие чаты (включая вложения)
        /// </summary>
        [HttpPost("forward")]
        [EndpointName("ForwardMessages")]
        [EndpointSummary("Пересылка сообщений")]
        [EndpointDescription("Копирует выбранные сообщения (текст + файлы на диске) в указанные чаты. До 20 сообщений и 5 чатов за раз.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ForwardMessagesAsync(
            [FromBody] ForwardMessagesRequest request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                    return error;

                if (request == null)
                    return BadRequest(new ErrorResponse { Error = "Тело запроса пустое или неверный JSON" });
                if (request.MessageIds == null || request.MessageIds.Count == 0)
                    return BadRequest(new ErrorResponse { Error = "Не указаны сообщения для пересылки" });
                if (request.TargetChatIds == null || request.TargetChatIds.Count == 0)
                    return BadRequest(new ErrorResponse { Error = "Не указаны целевые чаты" });
                if (request.MessageIds.Count > 20)
                    return BadRequest(new ErrorResponse { Error = "Можно переслать не более 20 сообщений за раз" });
                if (request.TargetChatIds.Count > 5)
                    return BadRequest(new ErrorResponse { Error = "Можно выбрать не более 5 чатов" });

                request.MessageIds = request.MessageIds.Where(id => id != Guid.Empty).Distinct().ToList();
                request.TargetChatIds = request.TargetChatIds.Where(id => id != Guid.Empty).Distinct().ToList();
                if (request.MessageIds.Count == 0)
                    return BadRequest(new ErrorResponse { Error = "Некорректные ID сообщений" });
                if (request.TargetChatIds.Count == 0)
                    return BadRequest(new ErrorResponse { Error = "Некорректные ID чатов" });

                var senderName = $"{user!.FirstName} {user.LastName}".Trim();
                if (string.IsNullOrWhiteSpace(senderName))
                    senderName = "Пользователь";

                var sourceMessages = new List<(Message msg, string? plainText, List<Attachment> attachments)>();
                foreach (var mid in request.MessageIds.Distinct())
                {
                    var msg = await _messageService.GetMessageByIdGlobalAsync(mid, cancellationToken);
                    if (msg == null)
                        return NotFound(new ErrorResponse { Error = $"Сообщение {mid} не найдено" });

                    var sourceChat = await _chatService.GetChatByIdAsync(msg.ChatId, cancellationToken);
                    if (sourceChat == null || !sourceChat.ChatParticipants.Any(p => p.UserId == user.UserId))
                        return Forbid();

                    var plain = string.IsNullOrEmpty(msg.MessageText)
                        ? null
                        : _encryptionService.TryDecryptSafe(msg.MessageText);

                    var atts = msg.Attachments?.ToList()
                        ?? (await _attachmentService.GetAttachmentsByMessageIdAsync(mid, cancellationToken)).ToList();
                    sourceMessages.Add((msg, plain, atts));
                }

                var targetChats = new List<Chat>();
                foreach (var chatId in request.TargetChatIds.Distinct())
                {
                    var chat = await _chatService.GetChatByIdAsync(chatId, cancellationToken);
                    if (chat == null)
                        return NotFound(new ErrorResponse { Error = $"Чат {chatId} не найден" });
                    if (!chat.ChatParticipants.Any(p => p.UserId == user.UserId))
                        return Forbid();
                    targetChats.Add(chat);
                }

                var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                Directory.CreateDirectory(uploadPath);
                var apiBase = (_configuration["URL:API:HTTPS"] ?? "").TrimEnd('/');

                int published = 0;
                var errors = new List<string>();

                foreach (var target in targetChats)
                {
                    foreach (var (srcMsg, plainText, attachments) in sourceMessages)
                    {
                        try
                        {
                            var originalSender = srcMsg.Sender != null
                                ? $"{srcMsg.Sender.FirstName} {srcMsg.Sender.LastName}".Trim()
                                : "Пользователь";
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
                                var srcPath = Path.Combine(uploadPath, diskName);
                                if (!System.IO.File.Exists(srcPath))
                                {
                                    _logger.LogWarning("Файл вложения не найден: {Path}", srcPath);
                                    continue;
                                }

                                var ext = Path.GetExtension(diskName);
                                if (string.IsNullOrEmpty(ext) && !string.IsNullOrEmpty(att.FileName))
                                    ext = Path.GetExtension(att.FileName);
                                var newFileName = Guid.NewGuid().ToString() + ext;
                                var destPath = Path.Combine(uploadPath, newFileName);
                                System.IO.File.Copy(srcPath, destPath, overwrite: true);

                                var newAttId = Guid.NewGuid();
                                var fileUrl = $"{apiBase}/uploads/{newFileName}";
                                var size = att.SizeInBytes ?? (int)new FileInfo(destPath).Length;

                                attachmentsInfo.Add(new AttachmentInfo
                                {
                                    AttachmentId = newAttId,
                                    FileName = att.FileName,
                                    FileType = att.FileType ?? "application/octet-stream",
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
                                SentAt = DateTime.UtcNow,
                                HasAttachments = attachmentsInfo.Count > 0,
                                Attachments = attachmentsInfo
                            }, cancellationToken);

                            published++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Ошибка пересылки сообщения {MessageId} в чат {ChatId}", srcMsg.MessageId, target.ChatId);
                            errors.Add(ex.Message);
                        }
                    }
                }

                if (published == 0)
                {
                    return StatusCode(500, new ErrorResponse
                    {
                        IsSuccess = false,
                        Error = errors.FirstOrDefault() ?? "Не удалось переслать сообщения"
                    });
                }

                return Ok(new
                {
                    IsSuccess = true,
                    published,
                    errors
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка ForwardMessages");
                return StatusCode(500, new ErrorResponse { IsSuccess = false, Error = ex.Message });
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
                    var name = Path.GetFileName(path.Split('?')[0]);
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                }
                catch { }
            }
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return Path.GetFileName(fileName);
            }
            return "file";
        }

        /// <summary>
        /// Пометить все сообщения чата как прочитанные (для текущего пользователя)
        /// </summary>
        [HttpPost("{chatId:guid}/read")]
        [EndpointName("MarkChatAsRead")]
        [EndpointSummary("Пометить все сообщения чата как прочитанные")]
        [EndpointDescription("Обновляет статус сообщения на прочитанный")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> MarkChatAsReadAsync([Description("Идентификатор чата")] Guid chatId,
            CancellationToken ct = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                    return error;

                var chat = await _chatService.GetChatByIdAsync(chatId, ct);
                if (chat == null)
                    return NotFound(new ErrorResponse { Error = "Чат не найден" });

                if (!chat.ChatParticipants.Any(p => p.UserId == user!.UserId))
                    return Forbid();

                var updated = await _messageService.MarkMessagesAsReadAsync(chatId, user!.UserId, ct);

                if (updated > 0)
                {
                    var readPayload = new
                    {
                        chatId,
                        readerId = user.UserId,
                        readAt = DateTime.UtcNow
                    };

                    await _hubContext.Clients.Group(chatId.ToString())
                        .SendAsync("MessagesRead", readPayload, ct);

                    foreach (var participant in chat.ChatParticipants)
                    {
                        await _hubContext.Clients.Group($"User_{participant.UserId}")
                            .SendAsync("MessagesRead", readPayload, ct);
                    }
                }

                return Ok(new { IsSuccess = true, UpdatedCount = updated });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Получить сообщения чата
        /// </summary>
        [HttpGet("{chatId:guid}")]
        [EndpointName("GetMessagesByChat")]
        [EndpointSummary("Получить сообщения чата")]
        [EndpointDescription(
            "Возвращает страницу сообщений. Параметр beforeSequence — SequenceNumber самого старого уже загруженного сообщения " +
            "(для подгрузки истории). Без параметра возвращаются последние limit сообщений.")]
        [ProducesResponseType(typeof(GetMessagesSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetMessagesByChatAsync([Description("Идентификатор чата (GUID)")] Guid chatId,
            [FromQuery, Description("Загрузить сообщения старше этого SequenceNumber")] long? beforeSequence = null,
            [FromQuery, Description("Размер страницы (1–200, по умолчанию 50)")] int limit = 50,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (messages, hasMore) = await _messageService.GetMessagesPagedAsync(
                    chatId, beforeSequence, limit, cancellationToken);

                var dtos = messages.Select(m => new MessageDto
                {
                    MessageId = m.MessageId,
                    ChatId = m.ChatId == Guid.Empty ? chatId : m.ChatId,
                    SenderId = m.SenderId,
                    SenderName = m.Sender != null
                        ? $"{m.Sender.FirstName} {m.Sender.LastName}".Trim()
                        : "Удалённый пользователь",
                    MessageText = string.IsNullOrEmpty(m.MessageText) ? null
                        : _encryptionService.TryDecryptSafe(m.MessageText),
                    SentAt = m.SendTime,
                    SequenceNumber = m.SequenceNumber,
                    Status = m.DeliveryStatus switch
                    {
                        MessageDeliveryStatus.Pending => "Pending",
                        MessageDeliveryStatus.Sent => "Sent",
                        MessageDeliveryStatus.Delivered => "Delivered",
                        MessageDeliveryStatus.Read => "Read",
                        MessageDeliveryStatus.Failed => "Failed",
                        _ => "Invalid status"
                    },
                    Attachments = m.Attachments.Select(a => new AttachmentDto
                    {
                        AttachmentId = a.AttachmentId,
                        FileName = a.FileName,
                        FileType = a.FileType ?? GetMimeType(a.FileName),
                        SizeInBytes = a.SizeInBytes ?? 0,
                        Url = a.Url
                    }).ToList(),
                    Reactions = (m.Reactions ?? Enumerable.Empty<Reaction>())
                        .GroupBy(r => r.ReactionType)
                        .Select(g => new ReactionSummaryDto
                        {
                            ReactionType = g.Key,
                            Count = g.Count(),
                            Users = g.Select(r => new ReactionUserDto
                            {
                                UserId = r.UserId,
                                UserName = r.User != null
                                    ? $"{r.User.FirstName} {r.User.LastName}".Trim()
                                    : null
                            }).ToList()
                        })
                        .OrderByDescending(x => x.Count)
                        .ToList()
                }).ToList();

                return Ok(new GetMessagesSuccessResponse
                {
                    IsSuccess = true,
                    Data = dtos,
                    HasMore = hasMore,
                    NextBeforeSequence = dtos.Count > 0 ? dtos.Min(d => d.SequenceNumber) : (long?)null
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Экспорт истории чата в разных форматах
        /// </summary>
        [HttpGet("{chatId:guid}/export")]
        [EndpointName("ExportChat")]
        [EndpointSummary("Экспорт истории чата")]
        [EndpointDescription(
            "Скачивает историю сообщений чата в одном из форматов: txt, json, html, csv. " +
            "Доступно только участникам чата. Максимум 5000 сообщений.")]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ExportChatAsync(
            [Description("Идентификатор чата (GUID)")] Guid chatId,
            [FromQuery, Description("Формат экспорта: txt | json | html | csv (по умолчанию txt)")] string format = "txt",
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                {
                    return error;
                }

                var participants = await _chatService.GetChatParticipantsAsync(chatId, cancellationToken);
                if (!participants.Any(p => p.UserId == user!.UserId))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                    {
                        IsSuccess = false,
                        Error = "Вы не являетесь участником этого чата"
                    });
                }

                var chat = await _chatService.GetChatByIdAsync(chatId, cancellationToken);
                if (chat == null)
                {
                    return NotFound(new ErrorResponse
                    {
                        IsSuccess = false,
                        Error = "Чат не найден"
                    });
                }

                var normalizedFormat = (format ?? "txt").Trim().ToLowerInvariant();
                if (normalizedFormat is not ("txt" or "json" or "html" or "csv"))
                {
                    return BadRequest(new ErrorResponse
                    {
                        IsSuccess = false,
                        Error = "Поддерживаемые форматы: txt, json, html, csv"
                    });
                }

                var result = await _messageService.ExportChatAsync(chatId, normalizedFormat, chat.Name, cancellationToken);

                return File(result.Content, result.ContentType, result.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка экспорта чата {ChatId}", chatId);
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Редактировать сообщение
        /// </summary>
        [HttpPut("{messageId}")]
        [EndpointName("UpdateMessage")]
        [EndpointSummary("Редактировать сообщение")]
        [EndpointDescription("Изменяет текст существующего сообщения. Доступно только отправителю.")]
        [ProducesResponseType(typeof(UpdateMessageSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateMessageAsync(
            [Description("Идентификатор сообщения")] Guid messageId,
            [FromBody, Description("Новый текст сообщения и ID чата")] UpdateMessageRequest request,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(request.MessageText))
            {
                return BadRequest(new ErrorResponse
                {
                    IsSuccess = false,
                    Error = "Текст не может быть пустым"
                });
            }

            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                {
                    return error;
                }

                var message = await _messageService.GetMessageByIdAsync(request.ChatId, messageId, ct);
                if (message == null)
                {
                    return NotFound(new ErrorResponse
                    {
                        IsSuccess = false,
                        Error = "Сообщение не найдено"
                    });
                }
                if (message.SenderId != user!.UserId)
                {
                    return Forbid();
                }

                message.MessageText = _encryptionService.Encrypt(request.MessageText);
                await _messageService.UpdateMessageAsync(message, ct);

                var updatedDto = new MessageDto
                {
                    MessageId = message.MessageId,
                    ChatId = request.ChatId,
                    SenderId = message.SenderId,
                    MessageText = request.MessageText.Trim(),
                    SentAt = message.SendTime,
                    Status = "Sent"
                };

                await _hubContext.Clients.Group(request.ChatId.ToString())
                    .SendAsync("ReceiveMessage", updatedDto, ct);

                return Ok(new UpdateMessageSuccessResponse
                {
                    IsSuccess = true,
                    Message = "Сообщение обновлено"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { IsSuccess = false, Error = ex.Message });
            }
        }

        /// <summary>
        /// Удалить сообщение
        /// </summary>
        [HttpDelete("{messageId}")]
        [EndpointName("DeleteMessage")]
        [EndpointSummary("Удалить сообщение")]
        [EndpointDescription("Удаляет сообщение. Доступно только отправителю. Уведомление рассылается через SignalR.")]
        [ProducesResponseType(typeof(DeleteMessageSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteMessageAsync(
            [Description("Идентификатор сообщения")] Guid messageId,
            [FromQuery, Description("Идентификатор чата (обязателен для проверки прав)")] Guid chatId,
            CancellationToken ct = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                {
                    return error;
                }

                var message = await _messageService.GetMessageByIdAsync(chatId, messageId, ct);
                if (message == null)
                {
                    return NotFound();
                }

                var chat = await _chatService.GetChatByIdAsync(chatId, ct);
                if (chat == null || !chat.ChatParticipants.Any(p => p.UserId == user!.UserId))
                {
                    return Forbid();
                }

                await _messageService.DeleteMessageAsync(messageId, ct);
                await _hubContext.Clients.Group(chatId.ToString())
                    .SendAsync("MessageDeleted", new { messageId, chatId });

                return Ok(new DeleteMessageSuccessResponse
                {
                    IsSuccess = true,
                    Message = "Сообщение удалено"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Удалить несколько сообщений
        /// </summary>
        [HttpPost("bulk-delete")]
        [EndpointName("BulkDeleteMessages")]
        [EndpointSummary("Удалить несколько сообщений")]
        [EndpointDescription("Удаляет указанные сообщения. Можно удалить только свои сообщения. Уведомления рассылаются через SignalR.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> BulkDeleteMessagesAsync(
            [FromBody] BulkDeleteMessagesRequest request,
            CancellationToken ct = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                    return error;

                if (request == null || request.MessageIds == null || request.MessageIds.Count == 0)
                {
                    return BadRequest(new ErrorResponse
                    {
                        IsSuccess = false,
                        Error = "Не указаны сообщения для удаления"
                    });
                }

                if (request.MessageIds.Count > 100)
                {
                    return BadRequest(new ErrorResponse
                    {
                        IsSuccess = false,
                        Error = "За один раз можно удалить не более 100 сообщений"
                    });
                }

                var chatId = request.ChatId;
                var allowed = new List<Guid>();

                foreach (var mid in request.MessageIds.Distinct())
                {
                    var message = await _messageService.GetMessageByIdAsync(chatId, mid, ct);
                    if (message == null)
                        continue;
                    allowed.Add(mid);
                }

                if (allowed.Count == 0)
                {
                    return NotFound(new ErrorResponse
                    {
                        IsSuccess = false,
                        Error = "Сообщения не найдены"
                    });
                }

                var deleted = await _messageService.DeleteMessagesAsync(allowed, ct);

                foreach (var mid in allowed)
                {
                    await _hubContext.Clients.Group(chatId.ToString())
                        .SendAsync("MessageDeleted", new { messageId = mid, chatId }, ct);
                }

                return Ok(new
                {
                    IsSuccess = true,
                    DeletedCount = deleted,
                    MessageIds = allowed
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка массового удаления сообщений");
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
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
    }
}
