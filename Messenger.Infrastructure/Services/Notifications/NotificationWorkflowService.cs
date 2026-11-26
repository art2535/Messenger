using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Interfaces.Notifications;

namespace Messenger.Infrastructure.Services.Notifications
{
    public sealed class NotificationWorkflowService : INotificationWorkflowService
    {
        private readonly INotificationService _notificationService;

        public NotificationWorkflowService(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        public Task<Guid> CreateNotificationAsync(
            Guid userId,
            string text,
            CancellationToken cancellationToken = default)
        {
            return _notificationService.CreateNotificationAsync(userId, text, cancellationToken);
        }

        public Task<IEnumerable<Notification>> GetNotificationsAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return _notificationService.GetNotificationsAsync(userId, cancellationToken);
        }
    }
}
