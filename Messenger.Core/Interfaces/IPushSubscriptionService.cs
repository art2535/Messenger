using Messenger.Core.DTOs.Push;
using Messenger.Core.Models;

namespace Messenger.Core.Interfaces
{
    public interface IPushSubscriptionService
    {
        Task<AccountSetting?> GetPushSettingsAsync(Guid userId, CancellationToken token = default);
        Task SavePushSettingsAsync(Guid userId, PushSubscriptionUpdateRequest request, CancellationToken token = default);
        Task AddSubscriptionAsync(PushSubscription subscription, CancellationToken ct = default);
        Task RemoveSubscriptionAsync(Guid id, CancellationToken ct = default);
        Task RemoveByEndpointAsync(string endpoint, CancellationToken ct = default);
        Task EnsurePushEnabledAsync(Guid userId, CancellationToken token = default);
        Task UpdateSubscriptionAsync(PushSubscription subscription, CancellationToken ct = default);
        Task SendPushToOfflineUsersAsync(Guid chatId, Guid senderId, string senderName, string? messageText, 
            bool hasAttachments, bool isMention = false, CancellationToken cancellationToken = default);
    }
}
