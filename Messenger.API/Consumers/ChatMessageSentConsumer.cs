using MassTransit;
using Messenger.Core.DTOs.Messages;
using Messenger.Core.Hubs;
using Messenger.Core.Interfaces;
using Messenger.Core.Messages;
using Messenger.Core.Models;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.API.Consumers
{
    public class ChatMessageSentConsumer : IConsumer<ChatMessageSent>
    {
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IMessageService _messageService;
        private readonly IAttachmentService _attachmentService;
        private readonly IEncryptionService _encryptionService;
        private readonly IChatService _chatService;
        private readonly ILogger<ChatMessageSentConsumer> _logger;
        private readonly IPushSubscriptionService _subscriptionService;

        public ChatMessageSentConsumer(IHubContext<ChatHub> hubContext, IMessageService messageService,
            IAttachmentService attachmentService, IEncryptionService encryptionService, IChatService chatService,
            ILogger<ChatMessageSentConsumer> logger, IPushSubscriptionService subscriptionService)
        {
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
            var msg = context.Message;

            _logger.LogInformation(
                "=== CONSUMER ПОЛУЧИЛ СООБЩЕНИЕ === MessageId={MessageId} ChatId={ChatId} SenderId={SenderId} HasText={HasText}",
                msg.MessageId, msg.ChatId, msg.SenderId, !string.IsNullOrEmpty(msg.MessageText));

            try
            {
                var serviceResult = await _messageService.SendMessageAsync(msg.MessageId, msg.ChatId, msg.SenderId,
                    msg.MessageText, msg.HasAttachments, null, context.CancellationToken);

                if (!serviceResult.isSuccess || serviceResult.data == null)
                    throw new Exception(serviceResult.error ?? "Unknown error");

                var savedMessage = serviceResult.data;

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
                        await _attachmentService.AddAttachmentAsync(attachment, context.CancellationToken);
                    }
                }

                savedMessage.DeliveryStatus = MessageDeliveryStatus.Delivered;
                await _messageService.UpdateMessageAsync(savedMessage, context.CancellationToken);

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
                    SentAt = savedMessage.SendTime,
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

                try
                {
                    var chatIdStr = msg.ChatId.ToString();
                    await _hubContext.Clients.Group(chatIdStr)
                        .SendAsync("ReceiveMessage", finalMessageDto, context.CancellationToken);
                    await _hubContext.Clients.Group(chatIdStr)
                        .SendAsync("MessageSendingStatus", new
                        {
                            MessageId = msg.MessageId,
                            ChatId = msg.ChatId,
                            Status = "Sent",
                            Timestamp = DateTimeOffset.UtcNow
                        }, context.CancellationToken);

                    var chat = await _chatService.GetChatByIdAsync(msg.ChatId, context.CancellationToken);
                    if (chat?.ChatParticipants != null)
                    {
                        foreach (var participant in chat.ChatParticipants)
                        {
                            var userGroup = $"User_{participant.UserId}";
                            await _hubContext.Clients.Group(userGroup)
                                .SendAsync("ReceiveMessage", finalMessageDto, context.CancellationToken);
                        }
                    }

                    _logger.LogInformation(
                        "SignalR ReceiveMessage отправлен MessageId={MessageId} ChatId={ChatId}",
                        msg.MessageId, msg.ChatId);
                }
                catch (Exception signalrEx)
                {
                    _logger.LogWarning(signalrEx,
                        "Не удалось отправить SignalR для сообщения {MessageId} в чат {ChatId}",
                        msg.MessageId, msg.ChatId);
                }

                try
                {
                    await _subscriptionService.SendPushToOfflineUsersAsync(
                        msg.ChatId, msg.SenderId, msg.SenderName ?? "Пользователь",
                        msg.MessageText, msg.HasAttachments, false, context.CancellationToken);
                }
                catch (Exception pushEx)
                {
                    _logger.LogWarning(pushEx, "Не удалось отправить push для чата {ChatId}", msg.ChatId);
                }

                _logger.LogInformation("Сообщение {MessageId} полностью обработано и доставлено", msg.MessageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ОШИБКА обработки сообщения {MessageId} в consumer", msg.MessageId);

                try
                {
                    var failedMessage = await _messageService.GetMessageByIdAsync(
                        msg.ChatId, msg.MessageId, context.CancellationToken);

                    if (failedMessage != null)
                    {
                        failedMessage.DeliveryStatus = MessageDeliveryStatus.Failed;
                        await _messageService.UpdateMessageAsync(failedMessage, context.CancellationToken);
                    }
                }
                catch (Exception innerEx)
                {
                    _logger.LogError(innerEx, "Не удалось обновить статус Failed для сообщения {MessageId}", msg.MessageId);
                }

                await _hubContext.Clients.Group(msg.ChatId.ToString())
                    .SendAsync("MessageSendingStatus", new
                    {
                        MessageId = msg.MessageId,
                        ChatId = msg.ChatId,
                        Status = "Failed",
                        Reason = "Ошибка обработки сообщения на сервере",
                        Timestamp = DateTimeOffset.UtcNow
                    }, context.CancellationToken);

                throw;
            }
        }
    }
}