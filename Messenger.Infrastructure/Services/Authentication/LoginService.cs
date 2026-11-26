using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Messenger.Infrastructure.Services.Authentication
{
    public class LoginService : ILoginService
    {
        private readonly GuapMessengerContext _context;

        public LoginService(GuapMessengerContext context)
        {
            _context = context;
        }

        public async Task CloseActiveLoginsForUsersAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default)
        {
            var ids = userIds.ToList();
            if (ids.Count == 0)
                return;

            await _context.Logins
                .Where(l => ids.Contains(l.UserId) && l.Active)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(l => l.Active, false)
                    .SetProperty(l => l.Token, string.Empty)
                    .SetProperty(l => l.LogoutTime, UtcDateTime.Now),
                    cancellationToken);
        }

        public async Task CloseActiveLoginsForIdleUsersAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default)
        {
            var ids = userIds.ToList();
            if (ids.Count == 0)
                return;

            var withIdleOn = await _context.AccountSettings
                .AsNoTracking()
                .Where(s => ids.Contains(s.AccountId) && s.IdleLogout)
                .Select(s => s.AccountId)
                .ToListAsync(cancellationToken);

            var hasSettings = await _context.AccountSettings
                .AsNoTracking()
                .Where(s => ids.Contains(s.AccountId))
                .Select(s => s.AccountId)
                .ToListAsync(cancellationToken);

            var withoutSettings = ids.Except(hasSettings).ToList();
            var toClose = withIdleOn.Concat(withoutSettings).Distinct().ToList();
            if (toClose.Count == 0)
                return;

            await _context.Logins
                .Where(l => toClose.Contains(l.UserId) && l.Active)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(l => l.Active, false)
                    .SetProperty(l => l.Token, string.Empty)
                    .SetProperty(l => l.LogoutTime, UtcDateTime.Now),
                    cancellationToken);
        }

        public async Task<bool?> IsSessionKeyActiveAsync(string sessionKey, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionKey))
                return null;

            var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionKey)));

            var login = await _context.Logins
                .AsNoTracking()
                .Where(l => l.Token == tokenHash)
                .OrderByDescending(l => l.LoginTime)
                .FirstOrDefaultAsync(cancellationToken);

            return login?.Active;
        }

        public async Task AddLoginAsync(Login login, CancellationToken cancellationToken = default)
        {
            await _context.Logins.AddAsync(login, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<IEnumerable<Login>> GetLoginsByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.Logins
                .Where(l => l.UserId == userId)
                .ToListAsync(cancellationToken);
        }

        public async Task UpdateLoginAsync(Login login, CancellationToken cancellationToken = default)
        {
            _context.Logins.Update(login);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<bool> RevokeLoginAsync(Guid userId, Guid loginId, CancellationToken cancellationToken = default)
        {
            var login = await _context.Logins
                .FirstOrDefaultAsync(l => l.LoginId == loginId && l.UserId == userId, cancellationToken);
            if (login == null)
                return false;

            login.Active = false;
            login.Token = string.Empty;
            login.LogoutTime = UtcDateTime.Now;

            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<int> RevokeOtherLoginsAsync(Guid userId, Guid? keepLoginId, CancellationToken cancellationToken = default)
        {
            var q = _context.Logins.Where(l => l.UserId == userId && l.Active);
            if (keepLoginId.HasValue)
                q = q.Where(l => l.LoginId != keepLoginId.Value);

            return await q.ExecuteUpdateAsync(s => s
                .SetProperty(l => l.Active, false)
                .SetProperty(l => l.Token, string.Empty)
                .SetProperty(l => l.LogoutTime, UtcDateTime.Now),
                cancellationToken);
        }

        public async Task<bool> RevokeLoginAndUpdateStatusAsync(Guid userId, Guid loginId, CancellationToken cancellationToken = default)
        {
            var login = await _context.Logins
                .FirstOrDefaultAsync(l => l.LoginId == loginId && l.UserId == userId, cancellationToken);

            if (login == null || !login.Active)
                return false;

            login.Active = false;
            login.Token = string.Empty;
            login.LogoutTime = DateTime.UtcNow;

            var hasOtherActiveSessions = await _context.Logins
                .AnyAsync(l => l.UserId == userId && l.LoginId != loginId && l.Active, cancellationToken);

            if (!hasOtherActiveSessions)
            {
                var userStatus = await _context.UserStatuses
                    .FirstOrDefaultAsync(us => us.UserId == userId, cancellationToken);

                if (userStatus != null)
                {
                    userStatus.Online = false;
                    userStatus.LastActivity = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<bool> ExpireSessionAsync(string sessionKey, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionKey))
                return false;

            // Как в IsSessionKeyActiveAsync — обязательно хешируем
            var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionKey)));

            var login = await _context.Logins
                .FirstOrDefaultAsync(l => l.Token == tokenHash && l.Active, cancellationToken);

            if (login == null)
                return false;

            login.Active = false;
            login.Token = string.Empty;          // обязательно очищаем
            login.LogoutTime = DateTime.UtcNow;

            // Не ставим Online = false, если у пользователя есть другие активные сессии
            var hasOtherActiveSessions = await _context.Logins
                .AnyAsync(l => l.UserId == login.UserId && l.LoginId != login.LoginId && l.Active, cancellationToken);

            if (!hasOtherActiveSessions)
            {
                var userStatus = await _context.UserStatuses
                    .FirstOrDefaultAsync(us => us.UserId == login.UserId, cancellationToken);

                if (userStatus != null)
                {
                    userStatus.Online = false;
                    userStatus.LastActivity = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
    }
}
