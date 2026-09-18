using Messenger.Core.DTOs.Users;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Messenger.Core.Interfaces
{
    public interface IUserService
    {
        Task<User?> GetUserByIdAsync(Guid id, CancellationToken token = default);
        Task<bool> IsBlockedByAsync(Guid blockerId, Guid blockedId, CancellationToken token = default);
        Task<IEnumerable<User>> GetBlockedUsersAsync(Guid userId, CancellationToken token = default);
        Task<IEnumerable<User>> GetAllUsersAsync(CancellationToken token = default);
        Task BlockUserAsync(Guid userId, Guid blockedUserId, CancellationToken token = default);
        Task UnblockUserAsync(Guid userId, Guid blockedUserId, CancellationToken token = default);
        Task UpdateProfileAsync(Guid userId, UpdateUserProfileRequest request, string? avatarUrl = null, 
            CancellationToken token = default);
        Task<string> UploadAvatarAsync(Guid userId, IFormFile file, CancellationToken token = default);
        Task DeleteAvatarAsync(Guid userId, CancellationToken token = default);
        Task DeleteAccountAsync(Guid userId, CancellationToken token = default);
        Task AssignRoleAsync(Guid userId, Guid roleId, CancellationToken token = default);
        Task<IEnumerable<Role>> GetRolesAsync(CancellationToken token = default);
        Task<IEnumerable<UserSearch>> SearchUsersAsync(string query, CancellationToken token = default);
        Task<User?> GetUserByExternalIdAsync(string externalId);
        Task<User> RegisterExternalUserAsync(string externalId, string email, string firstName, 
            string lastName, IEnumerable<string>? tokenRoles = null);
    }
}
