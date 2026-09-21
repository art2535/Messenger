using Messenger.Core.DTOs;
using Messenger.Core.DTOs.Messages;
using Messenger.Core.Messages;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Messenger.Core.Interfaces
{
    public interface IMessageService
    {
        Task<ServiceResult<Message>> SendMessageAsync(Guid messageId, Guid chatId, Guid senderId,
            string? content, bool hasAttachments, IFormFile[]? files = null, CancellationToken token = default);
        Task<IEnumerable<Message>> GetMessagesAsync(Guid chatId, CancellationToken token = default);
        Task<(IReadOnlyList<Message> Items, bool HasMore)> GetMessagesPagedAsync(Guid chatId, long? beforeSequence = null,
            int limit = 50, CancellationToken token = default);
        Task<Message?> GetMessageByIdAsync(Guid chatId, Guid messageId, CancellationToken token = default);
        Task DeleteMessageAsync(Guid messageId, CancellationToken token = default);
        Task<int> DeleteMessagesAsync(IEnumerable<Guid> messageIds, CancellationToken token = default);
        Task UpdateMessageAsync(Message message, CancellationToken token = default);
        Task<List<MessageDto>> SearchMessagesAsync(Guid chatId, string query, CancellationToken token = default);
        Task<int> MarkMessagesAsReadAsync(Guid chatId, Guid readerId, CancellationToken ct = default);
        Task<ChatExportResult> ExportChatAsync(Guid chatId, string format, string? chatName = null, CancellationToken token = default);
        Task PublishChatMessageAsync(ChatMessageSent message, CancellationToken cancellationToken = default);
    }
}
