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
        private readonly IMessageExportService _messageExportService;
        private readonly IMessageSearchService _messageSearchService;
        private readonly IMessagePinningService _messagePinningService;
        private readonly IMessageMutationService _messageMutationService;
        private readonly IMessageCreationService _messageCreationService;
        private readonly IMessageReadService _messageReadService;
        private readonly IMessageLookupService _messageLookupService;
        private readonly IMessageEventPublisher _messageEventPublisher;

        public MessageService(IMessagePaginationService messagePaginationService,
            IMessageExportService messageExportService,
            IMessageSearchService messageSearchService,
            IMessagePinningService messagePinningService,
            IMessageMutationService messageMutationService,
            IMessageCreationService messageCreationService,
            IMessageReadService messageReadService,
            IMessageLookupService messageLookupService,
            IMessageEventPublisher messageEventPublisher)
        {
            _messagePaginationService = messagePaginationService;
            _messageExportService = messageExportService;
            _messageSearchService = messageSearchService;
            _messagePinningService = messagePinningService;
            _messageMutationService = messageMutationService;
            _messageCreationService = messageCreationService;
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

        public async Task<ServiceResult<Message>> SendMessageAsync(Guid messageId, Guid chatId, Guid senderId,
            string? content, bool hasAttachments, IFormFile[]? files = null, CancellationToken token = default)
        {
            try
            {
                var message = new Message
                {
                    MessageId = messageId,
                    ChatId = chatId,
                    SenderId = senderId,
                    MessageText = content ?? string.Empty,
                    HasAttachments = hasAttachments,
                    SendTime = UtcDateTime.Now,
                    DeliveryStatus = MessageDeliveryStatus.Pending
                };

                await _messageCreationService.CreateMessageAsync(message, token);

                var savedMessage = await GetMessageByIdAsync(chatId, message.MessageId, token);

                return savedMessage != null
                    ? ServiceResult<Message>.Success(savedMessage)
                    : ServiceResult<Message>.Failure("Не удалось сохранить сообщение");
            }
            catch (Exception ex)
            {
                return ServiceResult<Message>.Failure(ex.Message, ex.InnerException?.Message);
            }
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

        public async Task<ChatExportResult> ExportChatAsync(Guid chatId, string format, string? chatName = null, 
            CancellationToken token = default)
        {
            format = (format ?? "txt").Trim().ToLowerInvariant();
            if (format is not ("txt" or "json" or "html" or "csv"))
                format = "txt";

            var allMessages = new List<Message>();
            long? beforeSeq = null;
            const int pageSize = 200;
            const int maxMessages = 5000;
            bool hasMore = true;

            while (hasMore && allMessages.Count < maxMessages)
            {
                var (batch, more) = await _messagePaginationService.GetMessagesByChatIdPagedAsync(chatId, beforeSeq, pageSize, token);
                if (batch.Count == 0)
                    break;

                allMessages.InsertRange(0, batch);
                beforeSeq = batch.Min(m => m.SequenceNumber);
                hasMore = more;

                if (batch.Count < pageSize)
                    hasMore = false;
            }

            allMessages = allMessages.OrderBy(m => m.SequenceNumber).ToList();

            return _messageExportService.Export(chatId, format, chatName, allMessages);
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