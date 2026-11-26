using Messenger.Core.DTOs.Messages;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;

namespace Messenger.Infrastructure.Services
{
    public sealed class MessageEditingWorkflowService : IMessageEditingWorkflowService
    {
        private readonly IMessageService _messageService;
        private readonly IEncryptionService _encryptionService;
        private readonly IMessageRealtimeNotifier _realtimeNotifier;

        public MessageEditingWorkflowService(
            IMessageService messageService,
            IEncryptionService encryptionService,
            IMessageRealtimeNotifier realtimeNotifier)
        {
            _messageService = messageService;
            _encryptionService = encryptionService;
            _realtimeNotifier = realtimeNotifier;
        }

        public async Task<MessageEditResult> EditAsync(
            Guid chatId,
            Guid messageId,
            Guid userId,
            string text,
            CancellationToken cancellationToken = default)
        {
            var message = await _messageService.GetMessageByIdAsync(
                chatId,
                messageId,
                cancellationToken);

            if (message == null)
            {
                return new MessageEditResult(MessageEditStatus.NotFound);
            }

            if (message.SenderId != userId)
            {
                return new MessageEditResult(MessageEditStatus.Forbidden);
            }

            message.MessageText = _encryptionService.Encrypt(text);
            await _messageService.UpdateMessageAsync(message, cancellationToken);

            var updatedDto = new MessageDto
            {
                MessageId = message.MessageId,
                ChatId = chatId,
                SenderId = message.SenderId,
                MessageText = text.Trim(),
                SentAt = UtcDateTime.AsUtc(message.SendTime),
                Status = "Sent"
            };

            await _realtimeNotifier.SendToChatAsync(
                chatId,
                "ReceiveMessage",
                updatedDto,
                cancellationToken);

            return new MessageEditResult(MessageEditStatus.Updated);
        }
    }
}
