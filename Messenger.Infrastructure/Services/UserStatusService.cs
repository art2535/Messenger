using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Services
{
    public class UserStatusService : IUserStatusService
    {
        private readonly GuapMessengerContext _context;

        public UserStatusService(GuapMessengerContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<UserStatus>> GetInactiveOnlineStatusesAsync(DateTime olderThan,
            CancellationToken cancellationToken = default)
        {
            return await _context.UserStatuses
                .Where(us => us.Online && us.LastActivity != null && us.LastActivity < olderThan)
                .ToListAsync(cancellationToken);
        }

        public async Task SetOfflineBatchAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default)
        {
            var ids = userIds.ToList();
            if (ids.Count == 0)
                return;

            await _context.UserStatuses
                .Where(us => ids.Contains(us.UserId) && us.Online)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(us => us.Online, false)
                    .SetProperty(us => us.LastActivity, DateTime.UtcNow),
                    cancellationToken);
        }

        public async Task UpdateUserStatusAsync(UserStatus userStatus, CancellationToken cancellationToken = default)
        {
            var rowsAffected = await _context.UserStatuses
                .Where(us => us.UserId == userStatus.UserId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(us => us.Online, userStatus.Online)
                    .SetProperty(us => us.LastActivity, DateTime.Now),
                    cancellationToken);

            if (rowsAffected == 0)
            {
                var newStatus = new UserStatus
                {
                    UserId = userStatus.UserId,
                    Online = userStatus.Online,
                    LastActivity = DateTime.Now
                };

                _context.UserStatuses.Add(newStatus);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<UserStatus?> GetUserStatusByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.UserStatuses
                .FirstOrDefaultAsync(us => us.UserId == userId, cancellationToken);
        }
    }
}
