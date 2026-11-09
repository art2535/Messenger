using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Services
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
                    .SetProperty(l => l.LogoutTime, UtcDateTime.Now)
                    .SetProperty(l => l.Token, string.Empty),
                    cancellationToken);
        }

        public async Task AddLoginAsync(Login login, CancellationToken cancellationToken = default)
        {
            await _context.Logins.AddAsync(login, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<IEnumerable<Login>> GetLoginsByUserIdAsync(Guid userId,
            CancellationToken cancellationToken = default)
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
            login.LogoutTime = UtcDateTime.Now;
            login.Token = string.Empty;
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
                .SetProperty(l => l.LogoutTime, UtcDateTime.Now)
                .SetProperty(l => l.Token, string.Empty),
                cancellationToken);
        }
    }
}
