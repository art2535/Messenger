using Messenger.Core.Models;

namespace Messenger.Infrastructure.Interfaces.Users
{
    public interface IUserStatusWorkflowService
    {
        Task<UserStatus?> GetUserStatusAsync(Guid userId, CancellationToken cancellationToken = default);
        Task UpdateUserStatusAsync(Guid userId, bool online, CancellationToken cancellationToken = default);
    }
}
