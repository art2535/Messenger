using Messenger.Core.Models;

namespace Messenger.Core.Interfaces
{
    public interface IUserStatusService
    {
        Task UpdateStatusAsync(UserStatus userStatus, CancellationToken cancellationToken = default);
        Task<UserStatus?> GetStatusByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<UserStatus>> GetInactiveOnlineStatusesAsync(DateTime olderThan,
            CancellationToken cancellationToken = default);
        Task SetOfflineBatchAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default);
    }
}
