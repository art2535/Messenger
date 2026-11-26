using Messenger.Core.DTOs;
using Messenger.Core.DTOs.Messages;
using Messenger.Core.Interfaces;
using Messenger.Core.Messages;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Messenger.Infrastructure.Services.MessageService.cs
{
    public sealed class MessageService : IMessageService
    {
        private readonly MessageQueryFacade _queryFacade;
        private readonly MessageCommandFacade _commandFacade;
        private readonly MessageReadFacade _readFacade;
        private readonly MessageExportFacade _exportFacade;

        public MessageService(
            MessageQueryFacade queryFacade,
            MessageCommandFacade commandFacade,
            MessageReadFacade readFacade,
            MessageExportFacade exportFacade)
        {
            _queryFacade = queryFacade;
            _commandFacade = commandFacade;
            _readFacade = readFacade;
            _exportFacade = exportFacade;
        }

        public Task PublishChatMessageAsync(
            ChatMessageSent message,
            CancellationToken cancellationToken = default)
        {
            return _commandFacade.PublishChatMessageAsync(message, cancellationToken);
        }

        public Task<List<MessageDto>> SearchMessagesAsync(
            Guid chatId,
            string query,
            CancellationToken token = default)
        {
            return _queryFacade.SearchMessagesAsync(chatId, query, token);
        }

        public Task<int> MarkMessagesAsReadAsync(
            Guid chatId,
            Guid readerId,
            CancellationToken ct = default)
        {
            return _readFacade.MarkMessagesAsReadAsync(chatId, readerId, ct);
        }

        public Task<IEnumerable<Message>> GetMessagesAsync(
            Guid chatId,
            CancellationToken token = default)
        {
            return _queryFacade.GetMessagesAsync(chatId, token);
        }

        public Task<(IReadOnlyList<Message> Items, bool HasMore)> GetMessagesPagedAsync(
            Guid chatId,
            long? beforeSequence = null,
            int limit = 50,
            CancellationToken token = default)
        {
            return _queryFacade.GetMessagesPagedAsync(chatId, beforeSequence, limit, token);
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
            return _commandFacade.SendMessageAsync(
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
            return _queryFacade.GetMessageByIdAsync(chatId, messageId, token);
        }

        public Task<Message?> GetMessageByIdGlobalAsync(
            Guid messageId,
            CancellationToken token = default)
        {
            return _queryFacade.GetMessageByIdGlobalAsync(messageId, token);
        }

        public Task DeleteMessageAsync(Guid messageId, CancellationToken token = default)
        {
            return _commandFacade.DeleteMessageAsync(messageId, token);
        }

        public Task UpdateMessageAsync(Message message, CancellationToken token = default)
        {
            return _commandFacade.UpdateMessageAsync(message, token);
        }

        public Task<ChatExportResult> ExportChatAsync(
            Guid chatId,
            string format,
            string? chatName = null,
            CancellationToken token = default)
        {
            return _exportFacade.ExportChatAsync(chatId, format, chatName, token);
        }

        public Task<int> DeleteMessagesAsync(
            IEnumerable<Guid> messageIds,
            CancellationToken token = default)
        {
            return _commandFacade.DeleteMessagesAsync(messageIds, token);
        }

        public Task<Message?> PinMessageAsync(
            Guid messageId,
            Guid userId,
            CancellationToken token = default)
        {
            return _commandFacade.PinMessageAsync(messageId, userId, token);
        }

        public Task<Message?> UnpinMessageAsync(
            Guid messageId,
            Guid userId,
            CancellationToken token = default)
        {
            return _commandFacade.UnpinMessageAsync(messageId, userId, token);
        }

        public Task<List<Message>> GetPinnedMessagesAsync(
            Guid chatId,
            CancellationToken token = default)
        {
            return _queryFacade.GetPinnedMessagesAsync(chatId, token);
        }
    }
}
