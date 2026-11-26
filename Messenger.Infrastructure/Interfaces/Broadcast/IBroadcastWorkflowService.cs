using Messenger.Core.DTOs.Broadcasts;

namespace Messenger.Infrastructure.Interfaces.Broadcast
{
    public interface IBroadcastWorkflowService
    {
        Task<object?> CreateBroadcastAsync(CreateBroadcastRequest request, Guid senderId);
        Task<object?> GetBroadcastSummaryAsync(Guid id, Guid userId, bool isAdmin);
        Task<object?> MarkAsReadAsync(Guid id, Guid userId);
        Task<object?> GetMyBroadcastsAsync(Guid userId, bool unreadOnly);
    }
}
