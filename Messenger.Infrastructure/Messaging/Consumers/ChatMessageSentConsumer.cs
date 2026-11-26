using Microsoft.Extensions.Logging;
using MassTransit;
using Messenger.Core.DTOs.Messages;
using Messenger.Core.Hubs;
using Messenger.Core.Interfaces;
using Messenger.Core.Messages;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Messenger.Infrastructure.Messaging.Consumers
{
    public class ChatMessageSentConsumer : IConsumer<ChatMessageSent>
    {
        private readonly GuapMessengerContext _context;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IMessageService _messageService;
        private readonly IAttachmentService _attachmentService;
        private readonly IEncryptionService _encryptionService;
        private readonly IChatService _chatService;
        private readonly ILogger<ChatMessageSentConsumer> _logger;
        private readonly IPushSubscriptionService _subscriptionService;

        public ChatMessageSentConsumer(
            GuapMessengerContext context,
            IHubContext<ChatHub> hubContext,
            IMessageService messageService,
            IAttachmentService attachmentService,
            IEncryptionService encryptionService,
            IChatService chatService,
            ILogger<ChatMessageSentConsumer> logger,
            IPushSubscriptionService subscriptionService)
        {
            _context = context;
            _hubContext = hubContext;
            _messageService = messageService;
            _attachmentService = attachmentService;
            _encryptionService = encryptionService;
            _chatService = chatService;
            _logger = logger;
            _subscriptionService = subscriptionService;

            _logger.LogInformation("=== ChatMessageSentConsumer успешно создан ===");
        }

        public async Task Consume(ConsumeContext<ChatMessageSent> context)
        {
            var messageId = context.Message.MessageId;
            var lockKey = BitConverter.ToInt64(messageId.ToByteArray(), 0);
            var lockAcquired = false;

            await _context.Database.OpenConnectionAsync(context.CancellationToken);

            try
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_lock({0})",
                    new object[] { lockKey },
                    context.CancellationToken);
                lockAcquired = true;

                await ProcessMessageAsync(context);
            }
            finally
            {
                try
                {
                    if (lockAcquired)
                    {
                        await _context.Database.ExecuteSqlRawAsync(
                            "SELECT pg_advisory_unlock({0})",
                            new object[] { lockKey },
                            CancellationToken.None);
                    }
                }
                finally
                {
                    await _context.Database.CloseConnectionAsync();
                }
            }
        }

        private async Task ProcessMessageAsync(ConsumeContext<ChatMessageSent> context)
        {
            var msg = context.Message;

            _logger.LogInformation(
                "=== CONSUMER ПОЛУЧИЛ СООБЩЕНИЕ === MessageId={MessageId} ChatId={ChatId} SenderId={SenderId} HasText={HasText}",
                msg.MessageId, msg.ChatId, msg.SenderId, !string.IsNullOrEmpty(msg.MessageText));

            try
            {
                var serviceResult = await _messageService.SendMessageAsync(
                    msg.MessageId,
                    msg.ChatId,
                    msg.SenderId,
                    msg.MessageText,
                    msg.HasAttachments,
                    null,
                    context.CancellationToken);

                if (!serviceResult.isSuccess || serviceResult.data == null)
                    throw new Exception(serviceResult.error ?? "Unknown error");

                var savedMessage = serviceResult.data;

                if (savedMessage.DeliveryStatus == MessageDeliveryStatus.Delivered)
                {
                    _logger.LogInformation(
                        "Повторная доставка уже обработанного сообщения {MessageId} пропущена",
                        msg.MessageId);
                    return;
                }

                if (msg.Attachments?.Any() == true)
                {
                    foreach (var attInfo in msg.Attachments)
                    {
                        var attachment = new Attachment
                        {
                            AttachmentId = attInfo.AttachmentId,
                            MessageId = savedMessage.MessageId,
                            FileName = attInfo.FileName,
                            FileType = attInfo.FileType,
                            SizeInBytes = (int)attInfo.SizeInBytes,
                            Url = attInfo.Url
                        };
                        await _attachmentService.AddAttachmentAsync(
                            attachment,
                            context.CancellationToken);
                    }
                }

                var decryptedText = string.IsNullOrEmpty(msg.MessageText)
                    ? null
                    : _encryptionService.TryDecryptSafe(msg.MessageText);

                var finalMessageDto = new MessageDto
                {
                    MessageId = savedMessage.MessageId,
                    ChatId = savedMessage.ChatId,
                    SenderId = savedMessage.SenderId,
                    SenderName = msg.SenderName ?? "Пользователь",
                    MessageText = decryptedText,
                    SentAt = UtcDateTime.AsUtc(savedMessage.SendTime),
                    SequenceNumber = savedMessage.SequenceNumber,
                    Status = "Sent",
                    Attachments = msg.Attachments?.Select(a => new AttachmentDto
                    {
                        AttachmentId = a.AttachmentId,
                        FileName = a.FileName,
                        FileType = a.FileType,
                        SizeInBytes = (int)a.SizeInBytes,
                        Url = a.Url
                    }).ToList() ?? new List<AttachmentDto>()
                };

                await using var transaction = await _context.Database.BeginTransactionAsync(
                    context.CancellationToken);

                savedMessage.DeliveryStatus = MessageDeliveryStatus.Delivered;
                await _messageService.UpdateMessageAsync(savedMessage, context.CancellationToken);

                _context.MessageNotificationOutbox.Add(new MessageNotificationOutbox
                {
                    OutboxId = Guid.NewGuid(),
                    MessageId = savedMessage.MessageId,
                    ChatId = savedMessage.ChatId,
                    SenderId = savedMessage.SenderId,
                    SenderName = msg.SenderName ?? "Пользователь",
                    Payload = JsonSerializer.Serialize(finalMessageDto),
                    MessageText = msg.MessageText,
                    HasAttachments = msg.HasAttachments,
                    Status = "Pending",
                    AttemptCount = 0,
                    CreatedAt = DateTime.UtcNow,
                    NextAttemptAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync(context.CancellationToken);
                await transaction.CommitAsync(context.CancellationToken);

                _logger.LogInformation(
                    "Сообщение {MessageId} полностью обработано и доставлено",
                    msg.MessageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "ОШИБКА обработки сообщения {MessageId} в consumer",
                    msg.MessageId);

                var shouldNotifyFailure = true;

                try
                {
                    var failedMessage = await _messageService.GetMessageByIdAsync(
                        msg.ChatId,
                        msg.MessageId,
                        context.CancellationToken);

                    if (failedMessage != null)
                    {
                        shouldNotifyFailure =
                            failedMessage.DeliveryStatus != MessageDeliveryStatus.Delivered;

                        if (shouldNotifyFailure)
                        {
                            failedMessage.DeliveryStatus = MessageDeliveryStatus.Failed;
                            await _messageService.UpdateMessageAsync(
                                failedMessage,
                                context.CancellationToken);
                        }
                    }
                }
                catch (Exception innerEx)
                {
                    _logger.LogError(innerEx,
                        "Не удалось обновить статус Failed для сообщения {MessageId}",
                        msg.MessageId);
                }

                if (shouldNotifyFailure)
                {
                    await _hubContext.Clients.Group(msg.ChatId.ToString())
                        .SendAsync("MessageSendingStatus", new
                        {
                            MessageId = msg.MessageId,
                            ChatId = msg.ChatId,
                            Status = "Failed",
                            Reason = "Ошибка обработки сообщения на сервере",
                            Timestamp = DateTimeOffset.UtcNow
                        }, context.CancellationToken);
                }

                throw;
            }
        }
    }
}
