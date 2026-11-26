using Messenger.Core.DTOs.Logins;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Interfaces.Authentication;
using System.Security.Cryptography;
using System.Text;

namespace Messenger.Infrastructure.Services.Authentication
{
    public sealed class LoginWorkflowService : ILoginWorkflowService
    {
        private readonly ILoginService _loginService;

        public LoginWorkflowService(ILoginService loginService)
        {
            _loginService = loginService;
        }

        public async Task<object> GetActiveLoginsAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var logins = await _loginService.GetLoginsByUserIdAsync(userId, cancellationToken);

            return logins
                .Where(login => login.Active)
                .OrderByDescending(login => login.LoginTime)
                .Select(login => (object)new
                {
                    LoginId = login.LoginId,
                    IpAddress = login.IpAddress,
                    LoginTime = login.LoginTime,
                    LogoutTime = login.LogoutTime,
                    Active = login.Active
                })
                .ToList();
        }

        public Task<bool> ExpireSessionAsync(
            string sessionKey,
            CancellationToken cancellationToken = default)
        {
            return _loginService.ExpireSessionAsync(sessionKey, cancellationToken);
        }

        public async Task<(Guid LoginId, string TokenHash)> CreateLoginAsync(
            Guid userId,
            CreateLoginRequest request,
            CancellationToken cancellationToken = default)
        {
            var rawToken = string.IsNullOrWhiteSpace(request.Token)
                ? Guid.NewGuid().ToString("N")
                : request.Token.Trim();

            var tokenHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

            var login = new Login
            {
                LoginId = Guid.NewGuid(),
                UserId = userId,
                Token = tokenHash,
                IpAddress = request.IpAddress,
                LoginTime = UtcDateTime.Now,
                Active = true
            };

            await _loginService.AddLoginAsync(login, cancellationToken);

            return (login.LoginId, tokenHash);
        }

        public async Task LogoutAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var logins = await _loginService.GetLoginsByUserIdAsync(userId, cancellationToken);
            var userLogout = logins.FirstOrDefault(login =>
                login.UserId == userId && login.Active == true);

            if (userLogout == null)
            {
                return;
            }

            userLogout.Token = string.Empty;
            userLogout.Active = false;
            userLogout.LogoutTime = UtcDateTime.Now;

            await _loginService.UpdateLoginAsync(userLogout, cancellationToken);
        }

        public Task<bool> RevokeLoginAsync(
            Guid userId,
            Guid loginId,
            CancellationToken cancellationToken = default)
        {
            return _loginService.RevokeLoginAndUpdateStatusAsync(
                userId,
                loginId,
                cancellationToken);
        }

        public Task<int> RevokeOtherLoginsAsync(
            Guid userId,
            Guid? keepLoginId,
            CancellationToken cancellationToken = default)
        {
            return _loginService.RevokeOtherLoginsAsync(userId, keepLoginId, cancellationToken);
        }

        public Task<bool?> IsSessionActiveAsync(
            string sessionKey,
            CancellationToken cancellationToken = default)
        {
            return _loginService.IsSessionKeyActiveAsync(sessionKey, cancellationToken);
        }
    }
}
