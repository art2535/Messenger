using Messenger.Application.DTOs;
using Messenger.Application.DTOs.Messages;
using Messenger.Application.Messages;
using Messenger.Application.Files;
using Messenger.Core.Enums;

namespace Messenger.Application.Interfaces
{
    public interface IMessageService
    {
        Task<ServiceResult<MessageDto>> SendMessageAsync(Guid messageId, Guid chatId, Guid senderId, string? content, bool hasAttachments, IUploadedFile[]? files = null, CancellationToken token = default);
        Task<IEnumerable<MessageDto>> GetMessagesAsync(Guid chatId, CancellationToken token = default);
        Task<(IReadOnlyList<MessageDto> Items, bool HasMore)> GetMessagesPagedAsync(Guid chatId, long? beforeSequence = null, int limit = 50, CancellationToken token = default);
        Task<MessageDto?> GetMessageByIdAsync(Guid chatId, Guid messageId, CancellationToken token = default);
        Task<MessageDto?> GetMessageByIdGlobalAsync(Guid messageId, CancellationToken token = default);
        Task DeleteMessageAsync(Guid messageId, Guid actorId, CancellationToken token = default);
        Task<int> DeleteMessagesAsync(IEnumerable<Guid> messageIds, Guid chatId, Guid actorId, CancellationToken token = default);
        Task UpdateMessageContentAsync(Guid messageId, Guid actorId, string encryptedContent, CancellationToken token = default);
        Task UpdateDeliveryStatusAsync(Guid messageId, MessageDeliveryStatus status, CancellationToken token = default);
        Task<List<MessageDto>> SearchMessagesAsync(Guid chatId, string query, CancellationToken token = default);
        Task<int> MarkMessagesAsReadAsync(Guid chatId, Guid readerId, CancellationToken ct = default);
        Task<ChatExportResult> ExportChatAsync(Guid chatId, string format, string? chatName = null, CancellationToken token = default);
        Task PublishChatMessageAsync(ChatMessageSent message, CancellationToken cancellationToken = default);
        Task<MessageDto?> PinMessageAsync(Guid messageId, Guid userId, CancellationToken token = default);
        Task<MessageDto?> UnpinMessageAsync(Guid messageId, Guid userId, CancellationToken token = default);
        Task<List<MessageDto>> GetPinnedMessagesAsync(Guid chatId, CancellationToken token = default);
    }
}