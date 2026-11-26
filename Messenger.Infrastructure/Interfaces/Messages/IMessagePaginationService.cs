using Messenger.Core.Models;

namespace Messenger.Infrastructure.Interfaces.Messages
{
    public interface IMessagePaginationService
    {
        Task<(IReadOnlyList<Message> Items, bool HasMore)> GetMessagesByChatIdPagedAsync(
            Guid chatId, long? beforeSequence = null, int limit = 50, CancellationToken token = default);
        Task InvalidateMessageCachesAsync(Guid chatId, CancellationToken token = default);
        Task PrependMessageToCachesAsync(Message message, CancellationToken token = default);
        Task InvalidateUserChatsCacheAsync(Guid chatId, CancellationToken token = default);
        Task InvalidateUserChatsCacheForUserAsync(Guid userId, CancellationToken token = default);
    }
}
