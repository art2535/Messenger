using Messenger.Core.DTOs.AccountSettings;
using Messenger.Core.Interfaces;
using Messenger.Infrastructure.Interfaces.Users;

namespace Messenger.Infrastructure.Services.Users
{
    public sealed class AccountSettingsWorkflowService : IAccountSettingsWorkflowService
    {
        private readonly IAccountSettingsService _accountSettingsService;

        public AccountSettingsWorkflowService(IAccountSettingsService accountSettingsService)
        {
            _accountSettingsService = accountSettingsService;
        }

        public Task<AccountSettingsDto> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return _accountSettingsService.GetByUserIdAsync(userId, cancellationToken);
        }

        public Task<AccountSettingsDto> UpdateAsync(
            Guid userId,
            UpdateAccountSettingsRequest request,
            CancellationToken cancellationToken = default)
        {
            return _accountSettingsService.UpdateAsync(userId, request, cancellationToken);
        }
    }
}
