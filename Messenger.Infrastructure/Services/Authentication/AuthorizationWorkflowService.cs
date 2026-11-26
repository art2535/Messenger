using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Interfaces.Authentication;

namespace Messenger.Infrastructure.Services.Authentication
{
    public sealed class AuthorizationWorkflowService : IAuthorizationWorkflowService
    {
        private readonly IUserService _userService;

        public AuthorizationWorkflowService(IUserService userService)
        {
            _userService = userService;
        }

        public async Task<User?> GetOrRegisterExternalUserAsync(
            string externalId,
            string? email,
            string? firstName,
            string? lastName,
            IEnumerable<string>? roles)
        {
            var user = await _userService.GetUserByExternalIdAsync(externalId);

            return user ?? await _userService.RegisterExternalUserAsync(
                externalId,
                email,
                firstName,
                lastName,
                roles);
        }
    }
}
