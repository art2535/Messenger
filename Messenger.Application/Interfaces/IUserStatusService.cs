using Messenger.Application.DTOs.UserStatuses;

namespace Messenger.Application.Interfaces;

public interface IUserStatusService
{
    Task UpdateUserStatusAsync(
        Guid userId,
        bool online,
        CancellationToken cancellationToken = default);

    Task UpdateActivityAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<UserStatusDto?> GetUserStatusByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserStatusDto>> GetInactiveOnlineStatusesAsync(
        DateTime olderThan,
        CancellationToken cancellationToken = default);

    Task SetOfflineBatchAsync(
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken = default);
}
