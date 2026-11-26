using Messenger.Core.Interfaces;

namespace Messenger.Infrastructure.Interfaces.Chats
{
    public enum ChatQueryStatus
    {
        Success,
        NotFound,
        Forbidden
    }

    public sealed record ChatQueryResult(
        ChatQueryStatus Status,
        object? Data = null,
        string? Error = null);

    public interface IChatQueryWorkflowService
    {
        Task<List<object>> GetUserChatsAsync(Guid userId, CancellationToken ct = default);

        Task<ChatQueryResult> GetChatDetailsAsync(Guid chatId, Guid userId,
            CancellationToken ct = default);
    }
}
