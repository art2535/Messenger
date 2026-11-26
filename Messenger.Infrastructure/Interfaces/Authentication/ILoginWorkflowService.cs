using Messenger.Core.DTOs.Logins;

namespace Messenger.Infrastructure.Interfaces.Authentication
{
    public interface ILoginWorkflowService
    {
        Task<object> GetActiveLoginsAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<bool> ExpireSessionAsync(string sessionKey, CancellationToken cancellationToken = default);
        Task<(Guid LoginId, string TokenHash)> CreateLoginAsync(
            Guid userId,
            CreateLoginRequest request,
            CancellationToken cancellationToken = default);
        Task LogoutAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<bool> RevokeLoginAsync(Guid userId, Guid loginId, CancellationToken cancellationToken = default);
        Task<int> RevokeOtherLoginsAsync(
            Guid userId,
            Guid? keepLoginId,
            CancellationToken cancellationToken = default);
        Task<bool?> IsSessionActiveAsync(string sessionKey, CancellationToken cancellationToken = default);
    }
}
