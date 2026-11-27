using Messenger.Application.DTOs.Broadcasts;

namespace Messenger.Application.Interfaces
{
    public interface IBroadcastService
    {
        Task<BroadcastCreatedResponse> CreateBroadcastAsync(
            CreateBroadcastRequest request,
            Guid senderId,
            CancellationToken cancellationToken = default);

        Task<BroadcastSummaryDto?> GetBroadcastSummaryAsync(
            Guid id,
            Guid currentUserId,
            bool isAdmin,
            CancellationToken cancellationToken = default);

        Task<MarkAsReadResponse> MarkAsReadAsync(
            Guid broadcastId,
            Guid userId,
            CancellationToken cancellationToken = default);

        Task<List<BroadcastInboxItemDto>> GetMyBroadcastsAsync(
            Guid userId,
            bool unreadOnly = true,
            CancellationToken cancellationToken = default);
    }
}
