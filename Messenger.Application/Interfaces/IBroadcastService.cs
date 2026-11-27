using Messenger.Application.DTOs.Broadcasts;

namespace Messenger.Application.Interfaces
{
    public interface IBroadcastService
    {
        Task<BroadcastCreatedResponse> CreateBroadcastAsync(CreateBroadcastRequest request, Guid senderId);
        Task<BroadcastSummaryDto?> GetBroadcastSummaryAsync(Guid id, Guid currentUserId, bool isAdmin);
        Task<MarkAsReadResponse> MarkAsReadAsync(Guid broadcastId, Guid userId);
        Task<List<object>> GetMyBroadcastsAsync(Guid userId, bool unreadOnly = true);
    }
}
