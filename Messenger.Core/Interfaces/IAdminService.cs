using Messenger.Core.DTOs.Admin;

namespace Messenger.Core.Interfaces
{
    public interface IAdminService
    {
        Task<AdminActivityStatsDto> GetActivityStatsAsync(CancellationToken ct = default);
        Task<bool> IsSystemAdminAsync(Guid userId, CancellationToken ct = default);
    }
}
