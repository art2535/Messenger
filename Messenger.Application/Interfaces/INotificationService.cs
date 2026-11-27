using Messenger.Application.DTOs.Notifications;

namespace Messenger.Application.Interfaces;

public interface INotificationService
{
    Task<Guid> CreateNotificationAsync(Guid userId, string text, CancellationToken token = default);
    Task<Guid> CreateNotificationAsync(Guid userId, string text, string deduplicationKey, CancellationToken token = default);
    Task<IEnumerable<NotificationDto>> GetNotificationsAsync(Guid userId, CancellationToken token = default);
    Task<NotificationDto?> GetNotificationAsync(Guid notificationId, CancellationToken token = default);
    Task MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken token = default);
}
