using Messenger.Core.DTOs.Push;
using Messenger.Core.Interfaces;
using Messenger.Infrastructure.Interfaces.Notifications;
using Microsoft.Extensions.Configuration;
using WebPush;

namespace Messenger.Infrastructure.Services.Notifications
{
    public sealed class PushWorkflowService : IPushWorkflowService
    {
        private readonly IPushSubscriptionService _subscriptionService;
        private readonly INotificationService _notificationService;
        private readonly VapidDetails _vapidDetails;

        public PushWorkflowService(
            IPushSubscriptionService subscriptionService,
            INotificationService notificationService,
            IConfiguration configuration)
        {
            _subscriptionService = subscriptionService;
            _notificationService = notificationService;

            var vapidSection = configuration.GetSection("Vapid");
            _vapidDetails = new VapidDetails(
                vapidSection["Subject"],
                vapidSection["PublicKey"]!,
                vapidSection["PrivateKey"]!);
        }

        public async Task SubscribeAsync(
            Guid userId,
            PushSubscriptionRequest request,
            CancellationToken cancellationToken = default)
        {
            var subscription = new Core.Models.PushSubscription
            {
                UserId = userId,
                Endpoint = request.Endpoint,
                P256dh = request.P256dh,
                Auth = request.Auth,
                CreatedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow
            };

            await _subscriptionService.RemoveByEndpointAsync(request.Endpoint, cancellationToken);
            await _subscriptionService.AddSubscriptionAsync(subscription, cancellationToken);
            await _subscriptionService.EnsurePushEnabledAsync(userId, cancellationToken);
        }

        public Task UnsubscribeAsync(
            string endpoint,
            CancellationToken cancellationToken = default)
        {
            return _subscriptionService.RemoveByEndpointAsync(endpoint, cancellationToken);
        }

        public Task MarkNotificationAsReadAsync(
            Guid notificationId,
            CancellationToken cancellationToken = default)
        {
            return _notificationService.MarkAsReadAsync(notificationId, cancellationToken);
        }

        public async Task<PushSubscriptionUpdateRequest> GetSettingsAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var settings = await _subscriptionService.GetPushSettingsAsync(userId, cancellationToken);

            return new PushSubscriptionUpdateRequest
            {
                PushEnabled = settings.PushEnabled,
                NotifyMessages = settings.NotifyMessages,
                NotifyGroupChats = settings.NotifyGroupChats,
                NotifyMentions = settings.NotifyMentions
            };
        }

        public Task SaveSettingsAsync(
            Guid userId,
            PushSubscriptionUpdateRequest request,
            CancellationToken cancellationToken = default)
        {
            return _subscriptionService.SavePushSettingsAsync(userId, request, cancellationToken);
        }

        public Task SendPushAsync(
            Guid senderId,
            SendPushNotificationRequest request,
            CancellationToken cancellationToken = default)
        {
            return _subscriptionService.SendPushToOfflineUsersAsync(
                request.ChatId,
                senderId,
                request.SenderName ?? "Пользователь",
                request.MessageText,
                request.HasAttachments,
                request.IsMention,
                cancellationToken);
        }

        public string GetVapidPublicKey()
        {
            return _vapidDetails.PublicKey;
        }
    }
}
