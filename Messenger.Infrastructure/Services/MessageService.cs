using Messenger.Core.DTOs;
using Messenger.Core.DTOs.Messages;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Core.Messages;
using Microsoft.AspNetCore.Http;

namespace Messenger.Infrastructure.Services
{
    public class MessageService : IMessageService
    {
        private readonly IMessagePaginationService _messagePaginationService;
        private readonly IMessageChatExportService _messageChatExportService;
        private readonly IMessageSearchService _messageSearchService;
        private readonly IMessagePinningService _messagePinningService;
        private readonly IMessageMutationService _messageMutationService;
        private readonly IMessageCreationWorkflowService _messageCreationWorkflowService;
        private readonly IMessageReadService _messageReadService;
        private readonly IMessageLookupService _messageLookupService;
        private readonly IMessageEventPublisher _messageEventPublisher;

        public MessageService(IMessagePaginationService messagePaginationService,
            IMessageChatExportService messageChatExportService,
            IMessageSearchService messageSearchService,
            IMessagePinningService messagePinningService,
            IMessageMutationService messageMutationService,
            IMessageCreationWorkflowService messageCreationWorkflowService,
            IMessageReadService messageReadService,
            IMessageLookupService messageLookupService,
            IMessageEventPublisher messageEventPublisher)
        {
            _messagePaginationService = messagePaginationService;
            _messageChatExportService = messageChatExportService;
            _messageSearchService = messageSearchService;
            _messagePinningService = messagePinningService;
            _messageMutationService = messageMutationService;
            _messageCreationWorkflowService = messageCreationWorkflowService;
            _messageReadService = messageReadService;
            _messageLookupService = messageLookupService;
            _messageEventPublisher = messageEventPublisher;
        }

        public Task PublishChatMessageAsync(
            ChatMessageSent message,
            CancellationToken cancellationToken = default)
        {
            return _messageEventPublisher.PublishChatMessageAsync(message, cancellationToken);
        }

        public Task<List<MessageDto>> SearchMessagesAsync(
            Guid chatId,
            string query,
            CancellationToken token = default)
        {
            return _messageSearchService.SearchMessagesAsync(chatId, query, token);
        }

        public Task<int> MarkMessagesAsReadAsync(
            Guid chatId,
            Guid readerId,
            CancellationToken ct = default)
        {
            return _messageReadService.MarkMessagesAsReadAsync(chatId, readerId, ct);
        }

        public async Task<IEnumerable<Message>> GetMessagesAsync(Guid chatId, CancellationToken token = default)
        {
            var (items, _) = await _messagePaginationService.GetMessagesByChatIdPagedAsync(chatId, null, 100, token);
            return items;
        }

        public async Task<(IReadOnlyList<Message> Items, bool HasMore)> GetMessagesPagedAsync(
            Guid chatId, long? beforeSequence = null, int limit = 50, CancellationToken token = default)
        {
            return await _messagePaginationService.GetMessagesByChatIdPagedAsync(chatId, beforeSequence, limit, token);
        }

        public Task<ServiceResult<Message>> SendMessageAsync(
            Guid messageId,
            Guid chatId,
            Guid senderId,
            string? content,
            bool hasAttachments,
            IFormFile[]? files = null,
            CancellationToken token = default)
        {
            return _messageCreationWorkflowService.SendMessageAsync(
                messageId,
                chatId,
                senderId,
                content,
                hasAttachments,
                token);
        }

        public Task<Message?> GetMessageByIdAsync(
            Guid chatId,
            Guid messageId,
            CancellationToken token = default)
        {
            return _messageLookupService.GetMessageByIdAsync(chatId, messageId, token);
        }

        public Task<Message?> GetMessageByIdGlobalAsync(
            Guid messageId,
            CancellationToken token = default)
        {
            return _messageLookupService.GetMessageByIdGlobalAsync(messageId, token);
        }

        public Task DeleteMessageAsync(Guid messageId, CancellationToken token = default)
        {
            return _messageMutationService.DeleteMessageAsync(messageId, token);
        }

        public Task UpdateMessageAsync(Message message, CancellationToken token = default)
        {
            return _messageMutationService.UpdateMessageAsync(message, token);
        }

        public Task<ChatExportResult> ExportChatAsync(
            Guid chatId,
            string format,
            string? chatName = null,
            CancellationToken token = default)
        {
            return _messageChatExportService.ExportChatAsync(chatId, format, chatName, token);
        }

        public Task<int> DeleteMessagesAsync(
            IEnumerable<Guid> messageIds,
            CancellationToken token = default)
        {
            return _messageMutationService.DeleteMessagesAsync(messageIds, token);
        }

        public Task<Message?> PinMessageAsync(
            Guid messageId,
            Guid userId,
            CancellationToken token = default)
        {
            return _messagePinningService.PinMessageAsync(messageId, userId, token);
        }

        public Task<Message?> UnpinMessageAsync(
            Guid messageId,
            Guid userId,
            CancellationToken token = default)
        {
            return _messagePinningService.UnpinMessageAsync(messageId, userId, token);
        }

        public Task<List<Message>> GetPinnedMessagesAsync(
            Guid chatId,
            CancellationToken token = default)
        {
            return _messagePinningService.GetPinnedMessagesAsync(chatId, token);
        }
    }
}