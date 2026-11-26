using Messenger.Core.DTOs.Users;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Messenger.Infrastructure.Interfaces.Users
{
    public interface IUserWorkflowService
    {
        Task<User?> GetByExternalIdAsync(string externalId);
        Task<object> SearchAsync(string query, CancellationToken cancellationToken = default);
        Task<IEnumerable<User>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<IEnumerable<Role>> GetRolesAsync(CancellationToken cancellationToken = default);
        Task<string?> GetDisplayNameAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<object?> GetCurrentUserInfoAsync(Guid userId, CancellationToken cancellationToken = default);
        Task UpdateProfileAsync(
            User user,
            UpdateUserProfileRequest request,
            string? avatarUrl,
            CancellationToken cancellationToken = default);
        Task<string> UploadAvatarAsync(
            User user,
            IFormFile avatarFile,
            CancellationToken cancellationToken = default);
        Task AssignRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default);
        Task DeleteAvatarAsync(User user, CancellationToken cancellationToken = default);
    }
}
