using Messenger.Core.Models;

namespace Messenger.Core.Interfaces
{
    public interface IChatService
    {
        Task<Chat> CreateChatAsync(string name, string type, Guid creatorId, CancellationToken token = default);
        Task<IEnumerable<Chat>> GetUserChatsAsync(Guid userId, CancellationToken token = default);
        Task AddParticipantToChatAsync(Guid chatId, Guid userId, string role, CancellationToken token = default);
        Task DeleteParticipantFromChatAsync(Guid chatId, Guid userId, CancellationToken token = default);
        Task<IEnumerable<ChatParticipant>> GetChatParticipantsAsync(Guid chatId, CancellationToken token = default);
        Task UpdateChatAsync(Chat chat, CancellationToken token = default);
        Task<Chat?> GetChatByIdAsync(Guid chatId, CancellationToken token = default);
        Task DeleteChatAsync(Chat chat, CancellationToken token = default);
        Task<List<object>> GetUserChatsWithLastMessageAsync(Guid userId, CancellationToken token = default);
        Task<IReadOnlyList<Guid>> GetPeerUserIdsAsync(Guid userId, CancellationToken token = default);
        Task<IReadOnlyList<Guid>> GetChatParticipantUserIdsAsync(Guid chatId, CancellationToken token = default);
    }
}
