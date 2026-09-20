using Asp.Versioning;
using MassTransit;
using Messenger.API.Responses;
using Messenger.API.Services;
using Messenger.Core.DTOs.Messages;
using Messenger.Core.Hubs;
using Messenger.Core.Interfaces;
using Messenger.Core.Messages;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
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
        private readonly ILogger<MessagesController> _logger;
        private readonly IPublishEndpoint _publishEndpoint;
        private readonly GuapMessengerContext _context;

        public MessagesController(IMessageService messageService, IConfiguration configuration,
            IHubContext<ChatHub> hubContext, IChatService chatService, IUserService userService,
            IEncryptionService encryptionService, ILogger<MessagesController> logger,
            IPublishEndpoint publishEndpoint, GuapMessengerContext context)
        {
            _messageService = messageService;
            _configuration = configuration;
            _hubContext = hubContext;
            _chatService = chatService;
            _userService = userService;
            _encryptionService = encryptionService;
            _logger = logger;
            _publishEndpoint = publishEndpoint;
            _context = context;
        }

        /// <summary>
        /// Поиск сообщений в чате
        /// </summary>
        [HttpGet("{chatId}/search")]
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
        [HttpPost("{chatId}")]
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

                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    await _publishEndpoint.Publish(new ChatMessageSent
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

                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    try
                    {
                        await transaction.RollbackAsync(cancellationToken);
                    }
                    catch (Exception rbEx)
                    {
                        _logger.LogWarning(rbEx, "Rollback не выполнен (транзакция уже завершена)");
                    }

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
        /// Пометить все сообщения чата как прочитанные (для текущего пользователя)
        /// </summary>
        [HttpPost("{chatId}/read")]
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
        [HttpGet("{chatId}")]
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
                    }).ToList()
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
        [HttpGet("{chatId}/export")]
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
                if (message.SenderId != user!.UserId)
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
                    if (message.SenderId != user!.UserId) 
                        continue;
                    allowed.Add(mid);
                }

                if (allowed.Count == 0)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                    {
                        IsSuccess = false,
                        Error = "Нет сообщений, которые можно удалить (только свои)"
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
