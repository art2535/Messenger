using Messenger.Core.DTOs.Users;
using Messenger.Application.Files;

namespace Messenger.Core.Interfaces
{
    public interface IUserService
    {
        Task<UserDto?> GetUserByIdAsync(Guid id, CancellationToken token = default);
        Task<IEnumerable<UserDto>> GetAllUsersAsync(CancellationToken token = default);
        Task UpdateProfileAsync(Guid userId, UpdateUserProfileRequest request, string? avatarUrl = null, 
            CancellationToken token = default);
        Task<string> UploadAvatarAsync(Guid userId, IUploadedFile file, CancellationToken token = default);
        Task DeleteAvatarAsync(Guid userId, CancellationToken token = default);
        Task DeleteAccountAsync(Guid userId, CancellationToken token = default);
        Task AssignRoleAsync(Guid userId, Guid roleId, CancellationToken token = default);
        Task<IEnumerable<RoleDto>> GetRolesAsync(CancellationToken token = default);
        Task<IEnumerable<UserSearch>> SearchUsersAsync(string query, CancellationToken token = default);
        Task<UserDto?> GetUserByExternalIdAsync(string externalId);
        Task<UserDto> RegisterExternalUserAsync(string externalId, string email, string firstName, 
            string lastName, IEnumerable<string>? tokenRoles = null);
    }
}
