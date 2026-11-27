using Messenger.Core.DTOs.Broadcasts;

namespace Messenger.Core.Interfaces
{
    public interface IBroadcastService
    {
        Task<BroadcastCreatedResponse> CreateBroadcastAsync(CreateBroadcastRequest request, Guid senderId);
        Task<BroadcastSummaryDto?> GetBroadcastSummaryAsync(Guid id, Guid currentUserId, bool isAdmin);
        Task<MarkAsReadResponse> MarkAsReadAsync(Guid broadcastId, Guid userId);
        Task<List<object>> GetMyBroadcastsAsync(Guid userId, bool unreadOnly = true);
    }
}
