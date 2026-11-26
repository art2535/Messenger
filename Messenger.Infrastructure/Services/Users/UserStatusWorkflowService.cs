using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Interfaces.Users;

namespace Messenger.Infrastructure.Services.Users
{
    public sealed class UserStatusWorkflowService : IUserStatusWorkflowService
    {
        private readonly IUserStatusService _userStatusService;

        public UserStatusWorkflowService(IUserStatusService userStatusService)
        {
            _userStatusService = userStatusService;
        }

        public Task<UserStatus?> GetUserStatusAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return _userStatusService.GetUserStatusByUserIdAsync(userId, cancellationToken);
        }

        public Task UpdateUserStatusAsync(
            Guid userId,
            bool online,
            CancellationToken cancellationToken = default)
        {
            var userStatus = new UserStatus
            {
                UserId = userId,
                Online = online,
                LastActivity = DateTime.UtcNow
            };

            return _userStatusService.UpdateUserStatusAsync(userStatus, cancellationToken);
        }
    }
}
