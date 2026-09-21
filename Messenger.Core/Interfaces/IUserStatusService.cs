using Messenger.Core.Models;

namespace Messenger.Core.Interfaces
{
    public interface IUserStatusService
    {
        Task UpdateUserStatusAsync(UserStatus userStatus, CancellationToken cancellationToken = default);
        Task<UserStatus?> GetUserStatusByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<UserStatus>> GetInactiveOnlineStatusesAsync(DateTime olderThan,
            CancellationToken cancellationToken = default);
        Task SetOfflineBatchAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default);
    }
}
