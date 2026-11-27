using Messenger.Application.Common;
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
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0)
                return;

            await _context.UserStatuses
                .Where(us => ids.Contains(us.UserId) && us.Online)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(us => us.Online, false)
                    .SetProperty(us => us.LastActivity, UtcDateTime.Now),
                    cancellationToken);
        }

        public async Task SetOnlineBatchAsync(
            IEnumerable<Guid> userIds,
            CancellationToken cancellationToken = default)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0)
                return;

            await _context.UserStatuses
                .Where(us => ids.Contains(us.UserId) && !us.Online)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(us => us.Online, true)
                    .SetProperty(us => us.LastActivity, UtcDateTime.Now),
                    cancellationToken);
        }

        public async Task UpdateUserStatusAsync(
            Guid userId,
            bool online,
            CancellationToken cancellationToken = default)
        {
            var now = UtcDateTime.Now;

            // UPSERT атомарен: параллельные подключения одного пользователя
            // не пытаются одновременно вставить одну и ту же строку статуса.
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""User_Statuses"" (""User_ID"", ""Online"", ""Last_Activity"")
                VALUES ({userId}, {online}, {now})
                ON CONFLICT (""User_ID"")
                DO UPDATE SET
                    ""Online"" = EXCLUDED.""Online"",
                    ""Last_Activity"" = EXCLUDED.""Last_Activity"";",
                cancellationToken);
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
