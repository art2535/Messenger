using Messenger.Core.DTOs.AccountSettings;

namespace Messenger.Infrastructure.Interfaces.Users
{
    public interface IAccountSettingsWorkflowService
    {
        Task<AccountSettingsDto> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default);

        Task<AccountSettingsDto> UpdateAsync(
            Guid userId,
            UpdateAccountSettingsRequest request,
            CancellationToken cancellationToken = default);
    }
}
