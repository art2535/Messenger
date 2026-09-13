using Messenger.Core.Models;

namespace Messenger.Core.Interfaces
{
    public interface ILoginService
    {
        Task AddLoginAsync(Login login, CancellationToken cancellationToken = default);
        Task<IEnumerable<Login>> GetLoginsByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
        Task UpdateLoginAsync(Login login, CancellationToken cancellationToken = default);
        Task CloseActiveLoginsForUsersAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default);
    }
}
