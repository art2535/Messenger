using Messenger.Application.DTOs.Chats;

namespace Messenger.Application.Interfaces;

public interface IChatService
{
    Task<ChatDto> CreateChatAsync(string name, string type, Guid creatorId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ChatDto>> GetUserChatsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AddParticipantToChatAsync(Guid chatId, Guid actorId, Guid userId, string role, CancellationToken cancellationToken = default);
    Task DeleteParticipantFromChatAsync(Guid chatId, Guid actorId, Guid userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ChatParticipantDto>> GetChatParticipantsAsync(Guid chatId, CancellationToken cancellationToken = default);
        Task<IEnumerable<ChatParticipantDto>> GetChatParticipantsAsync(Guid chatId, Guid actorId, CancellationToken cancellationToken = default);
    Task UpdateChatAsync(UpdateChatDto chat, Guid actorId, CancellationToken cancellationToken = default);
    Task<ChatDto?> GetChatByIdAsync(Guid chatId, CancellationToken cancellationToken = default);
        Task<ChatDto?> GetChatByIdAsync(Guid chatId, Guid actorId, CancellationToken cancellationToken = default);
    Task DeleteChatAsync(Guid chatId, Guid actorId, CancellationToken cancellationToken = default);
    Task<List<ChatListItemDto>> GetUserChatsWithLastMessageAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetPeerUserIdsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetChatParticipantUserIdsAsync(Guid chatId, CancellationToken cancellationToken = default);
}