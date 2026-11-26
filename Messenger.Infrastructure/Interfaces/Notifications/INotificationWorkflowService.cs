using Messenger.Core.Models;

namespace Messenger.Infrastructure.Interfaces.Notifications
{
    public interface INotificationWorkflowService
    {
        Task<Guid> CreateNotificationAsync(
            Guid userId,
            string text,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<Notification>> GetNotificationsAsync(
            Guid userId,
            CancellationToken cancellationToken = default);
    }
}
