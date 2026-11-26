using Messenger.Core.DTOs.Push;

namespace Messenger.Infrastructure.Interfaces.Notifications
{
    public interface IPushWorkflowService
    {
        Task SubscribeAsync(Guid userId, PushSubscriptionRequest request, CancellationToken cancellationToken = default);
        Task UnsubscribeAsync(string endpoint, CancellationToken cancellationToken = default);
        Task MarkNotificationAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default);
        Task<PushSubscriptionUpdateRequest> GetSettingsAsync(
            Guid userId,
            CancellationToken cancellationToken = default);
        Task SaveSettingsAsync(
            Guid userId,
            PushSubscriptionUpdateRequest request,
            CancellationToken cancellationToken = default);
        Task SendPushAsync(
            Guid senderId,
            SendPushNotificationRequest request,
            CancellationToken cancellationToken = default);
        string GetVapidPublicKey();
    }
}
