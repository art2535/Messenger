using Messenger.Application.DTOs.AccountSettings;

namespace Messenger.Application.Interfaces
{
    public interface IAccountSettingsService
    {
        Task<AccountSettingsDto> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<AccountSettingsDto> UpdateAsync(Guid userId, UpdateAccountSettingsRequest request, CancellationToken cancellationToken = default);
    }
}
