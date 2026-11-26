using Messenger.Core.Models;

namespace Messenger.Infrastructure.Interfaces.Authentication
{
    public interface IAuthorizationWorkflowService
    {
        Task<User?> GetOrRegisterExternalUserAsync(
            string externalId,
            string? email,
            string? firstName,
            string? lastName,
            IEnumerable<string>? roles);
    }
}
