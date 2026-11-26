using Messenger.Core.Interfaces;
using Messenger.Core.Models;

namespace Messenger.Infrastructure.Services
{
    public sealed class MessageControllerWorkflowService : IMessageControllerWorkflowService
    {
        private readonly IMessageService _messageService;
        private readonly IChatService _chatService;
        private readonly IAccountSettingsService _accountSettingsService;
        private readonly IEncryptionService _encryptionService;
        private readonly IMessageRealtimeNotifier _realtimeNotifier;

        public MessageControllerWorkflowService(
            IMessageService messageService,
            IChatService chatService,
            IAccountSettingsService accountSettingsService,
            IEncryptionService encryptionService,
            IMessageRealtimeNotifier realtimeNotifier)
        {
            _messageService = messageService;
            _chatService = chatService;
            _accountSettingsService = accountSettingsService;
            _encryptionService = encryptionService;
            _realtimeNotifier = realtimeNotifier;
        }

        public async Task<BulkMessageDeletionResult> DeleteManyAsync(
            Guid chatId,
            IEnumerable<Guid> messageIds,
            CancellationToken cancellationToken = default)
        {
            var allowedIds = new List<Guid>();

            foreach (var messageId in messageIds.Distinct())
            {
                var message = await _messageService.GetMessageByIdAsync(
                    chatId,
                    messageId,
                    cancellationToken);
                if (message != null)
                {
                    allowedIds.Add(messageId);
                }
            }

            if (allowedIds.Count == 0)
            {
                return new BulkMessageDeletionResult(
                    MessageWorkflowStatus.NotFound,
                    0,
                    allowedIds);
            }

            var deleted = await _messageService.DeleteMessagesAsync(allowedIds, cancellationToken);
            var chat = await _chatService.GetChatByIdAsync(chatId, cancellationToken);
            var participants = chat?.ChatParticipants?.ToList() ?? new List<ChatParticipant>();

            foreach (var messageId in allowedIds)
            {
                var payload = new
                {
                    MessageId = messageId,
                    ChatId = chatId
                };

                await _realtimeNotifier.SendToChatAndUsersAsync(
                    chatId,
                    participants.Select(participant => participant.UserId),
                    "MessageDeleted",
                    payload,
                    cancellationToken);
            }

            return new BulkMessageDeletionResult(
                MessageWorkflowStatus.Success,
                deleted,
                allowedIds);
        }

        public async Task<ChatReadResult> MarkChatAsReadAsync(
            Guid chatId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var chat = await _chatService.GetChatByIdAsync(chatId, cancellationToken);
            if (chat == null)
            {
                return new ChatReadResult(MessageWorkflowStatus.NotFound, 0);
            }

            if (!chat.ChatParticipants.Any(participant => participant.UserId == userId))
            {
                return new ChatReadResult(MessageWorkflowStatus.Forbidden, 0);
            }

            var updated = await _messageService.MarkMessagesAsReadAsync(
                chatId,
                userId,
                cancellationToken);

            if (updated > 0)
            {
                var sendReceipts = true;
                try
                {
                    var settings = await _accountSettingsService.GetByUserIdAsync(
                        userId,
                        cancellationToken);
                    sendReceipts = settings.ReadReceipts;
                }
                catch
                {
                }

                if (sendReceipts)
                {
                    var readPayload = new
                    {
                        ChatId = chatId,
                        ReaderId = userId,
                        ReadAt = DateTime.UtcNow
                    };

                    await _realtimeNotifier.SendToChatAndUsersAsync(
                        chatId,
                        chat.ChatParticipants.Select(participant => participant.UserId),
                        "MessagesRead",
                        readPayload,
                        cancellationToken);
                }
            }

            return new ChatReadResult(MessageWorkflowStatus.Success, updated);
        }

        public async Task<PinnedMessageResult> SetPinnedAsync(
            Guid messageId,
            Guid userId,
            bool isPinned,
            CancellationToken cancellationToken = default)
        {
            Message? message;
            try
            {
                message = isPinned
                    ? await _messageService.PinMessageAsync(messageId, userId, cancellationToken)
                    : await _messageService.UnpinMessageAsync(messageId, userId, cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                return new PinnedMessageResult(MessageWorkflowStatus.Forbidden, null, Guid.Empty);
            }

            if (message == null)
            {
                return new PinnedMessageResult(MessageWorkflowStatus.NotFound, null, Guid.Empty);
            }

            var text = isPinned && !string.IsNullOrEmpty(message.MessageText)
                ? _encryptionService.TryDecryptSafe(message.MessageText)
                : null;
            var payload = new
            {
                MessageId = message.MessageId,
                ChatId = message.ChatId,
                IsPinned = isPinned,
                PinnedAt = isPinned && message.PinnedAt.HasValue
                    ? UtcDateTime.AsUtc(message.PinnedAt.Value)
                    : (DateTime?)null,
                Text = isPinned
                    ? (text ?? string.Empty).Length > 200
                        ? (text ?? string.Empty)[..200]
                        : (text ?? string.Empty)
                    : null,
                PinnedBy = userId
            };

            await _realtimeNotifier.SendToChatAsync(
                message.ChatId,
                "MessagePinned",
                payload,
                cancellationToken);

            var chat = await _chatService.GetChatByIdAsync(message.ChatId, cancellationToken);
            if (chat?.ChatParticipants != null)
            {
                await _realtimeNotifier.SendToUsersAsync(
                    chat.ChatParticipants.Select(participant => participant.UserId),
                    "MessagePinned",
                    payload,
                    cancellationToken);
            }

            return new PinnedMessageResult(MessageWorkflowStatus.Success, payload, message.ChatId);
        }

        public async Task<PinnedMessagesResult> GetPinnedAsync(
            Guid chatId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var chat = await _chatService.GetChatByIdAsync(chatId, cancellationToken);
            if (chat == null || !chat.ChatParticipants.Any(
                participant => participant.UserId == userId))
            {
                return new PinnedMessagesResult(
                    MessageWorkflowStatus.Forbidden,
                    Array.Empty<PinnedMessageDto>());
            }

            var messages = await _messageService.GetPinnedMessagesAsync(chatId, cancellationToken);
            var result = messages.Select(message =>
            {
                var decrypted = string.IsNullOrEmpty(message.MessageText)
                    ? null
                    : _encryptionService.TryDecryptSafe(message.MessageText);

                return new PinnedMessageDto(
                    message.MessageId,
                    message.ChatId,
                    (decrypted ?? string.Empty).Length > 200
                        ? (decrypted ?? string.Empty)[..200]
                        : decrypted ?? string.Empty,
                    message.PinnedAt.HasValue
                        ? UtcDateTime.AsUtc(message.PinnedAt.Value)
                        : null,
                    message.SequenceNumber,
                    message.SenderId,
                    message.Sender != null
                        ? $"{message.Sender.FirstName} {message.Sender.LastName}".Trim()
                        : null);
            }).ToList();

            return new PinnedMessagesResult(MessageWorkflowStatus.Success, result);
        }
    }
}
