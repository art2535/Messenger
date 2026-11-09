using Messenger.Core.DTOs.AccountSettings;

namespace Messenger.Core.Interfaces
{
    public interface IAccountSettingsService
    {
        Task<AccountSettingsDto> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<AccountSettingsDto> UpdateAsync(Guid userId, UpdateAccountSettingsRequest request, CancellationToken cancellationToken = default);
    }
}
