using Messenger.Core.DTOs.Broadcasts;
using Messenger.Core.Interfaces;
using Messenger.Infrastructure.Interfaces.Broadcast;

namespace Messenger.Infrastructure.Services.Broadcast
{
    public sealed class BroadcastWorkflowService : IBroadcastWorkflowService
    {
        private readonly IBroadcastService _broadcastService;

        public BroadcastWorkflowService(IBroadcastService broadcastService)
        {
            _broadcastService = broadcastService;
        }

        public async Task<object?> CreateBroadcastAsync(CreateBroadcastRequest request, Guid senderId)
        {
            return await _broadcastService.CreateBroadcastAsync(request, senderId);
        }

        public async Task<object?> GetBroadcastSummaryAsync(Guid id, Guid userId, bool isAdmin)
        {
            return await _broadcastService.GetBroadcastSummaryAsync(id, userId, isAdmin);
        }

        public async Task<object?> MarkAsReadAsync(Guid id, Guid userId)
        {
            return await _broadcastService.MarkAsReadAsync(id, userId);
        }

        public async Task<object?> GetMyBroadcastsAsync(Guid userId, bool unreadOnly)
        {
            return await _broadcastService.GetMyBroadcastsAsync(userId, unreadOnly);
        }
    }
}
