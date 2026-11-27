using Messenger.Application.DTOs.Logins;

namespace Messenger.Application.Interfaces;

public interface ILoginService
{
    Task AddLoginAsync(CreateLoginDto login, CancellationToken cancellationToken = default);
    Task<IEnumerable<LoginDto>> GetLoginsByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> CloseLoginAsync(Guid userId, Guid loginId, CancellationToken cancellationToken = default);
    Task CloseActiveLoginsForUsersAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default);
    Task CloseActiveLoginsForIdleUsersAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default);
    Task<bool> RevokeLoginAsync(Guid userId, Guid loginId, CancellationToken cancellationToken = default);
    Task<int> RevokeOtherLoginsAsync(Guid userId, Guid? keepLoginId, CancellationToken cancellationToken = default);
    Task<bool?> IsSessionKeyActiveAsync(string sessionKey, CancellationToken cancellationToken = default);
    Task<bool> RevokeLoginAndUpdateStatusAsync(Guid userId, Guid loginId, CancellationToken cancellationToken = default);
    Task<bool> ExpireSessionAsync(string sessionKey, CancellationToken cancellationToken = default);
}
