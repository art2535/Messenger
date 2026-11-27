using Messenger.Application.DTOs.UserStatuses;
using Messenger.Application.Interfaces;
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

        public async Task<IReadOnlyList<UserStatusDto>> GetInactiveOnlineStatusesAsync(
            DateTime olderThan,
            CancellationToken cancellationToken = default)
        {
            return await _context.UserStatuses
                .AsNoTracking()
                .Where(us => us.Online && us.LastActivity != null && us.LastActivity < olderThan)
                .Select(us => new UserStatusDto
                {
                    UserId = us.UserId,
                    Online = us.Online,
                    LastActivity = us.LastActivity
                })
                .ToListAsync(cancellationToken);
        }

        public async Task SetOfflineBatchAsync(
            IEnumerable<Guid> userIds,
            CancellationToken cancellationToken = default)
        {
            var ids = userIds.ToList();
            if (ids.Count == 0)
                return;

            await _context.UserStatuses
                .Where(us => ids.Contains(us.UserId) && us.Online)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(us => us.Online, false)
                    .SetProperty(us => us.LastActivity, UtcDateTime.Now),
                    cancellationToken);
        }

        public async Task UpdateUserStatusAsync(
            Guid userId,
            bool online,
            CancellationToken cancellationToken = default)
        {
            var now = UtcDateTime.Now;

            var rowsAffected = await _context.UserStatuses
                .Where(us => us.UserId == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(us => us.Online, online)
                    .SetProperty(us => us.LastActivity, now),
                    cancellationToken);

            if (rowsAffected == 0)
            {
                var newStatus = new Messenger.Core.Models.UserStatus
                {
                    UserId = userId,
                    Online = online,
                    LastActivity = now
                };

                _context.UserStatuses.Add(newStatus);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task UpdateActivityAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            await _context.UserStatuses
                .Where(us => us.UserId == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(us => us.LastActivity, UtcDateTime.Now),
                    cancellationToken);
        }

        public async Task<UserStatusDto?> GetUserStatusByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return await _context.UserStatuses
                .AsNoTracking()
                .Where(us => us.UserId == userId)
                .Select(us => new UserStatusDto
                {
                    UserId = us.UserId,
                    Online = us.Online,
                    LastActivity = us.LastActivity
                })
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}
