using Messenger.Core.DTOs.Users;
using Messenger.Core.Hubs;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Interfaces.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.Infrastructure.Services.Users
{
    public sealed class UserWorkflowService : IUserWorkflowService
    {
        private const string DefaultAvatarUrl = "https://static.photos/people/200x200/default";

        private readonly IUserService _userService;
        private readonly IChatService _chatService;
        private readonly IHubContext<ChatHub> _hubContext;

        public UserWorkflowService(
            IUserService userService,
            IChatService chatService,
            IHubContext<ChatHub> hubContext)
        {
            _userService = userService;
            _chatService = chatService;
            _hubContext = hubContext;
        }

        public Task<User?> GetByExternalIdAsync(string externalId)
        {
            return _userService.GetUserByExternalIdAsync(externalId);
        }

        public async Task<object> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            var result = await _userService.SearchUsersAsync(query.Trim(), cancellationToken);

            return result.Select(user => new
            {
                Id = user.Id,
                Name = user.Name,
                Avatar = user.Avatar ?? DefaultAvatarUrl
            });
        }

        public Task<IEnumerable<User>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return _userService.GetAllUsersAsync(cancellationToken);
        }

        public Task<IEnumerable<Role>> GetRolesAsync(CancellationToken cancellationToken = default)
        {
            return _userService.GetRolesAsync(cancellationToken);
        }

        public async Task<string?> GetDisplayNameAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var user = await _userService.GetUserByIdAsync(userId, cancellationToken);
            return user == null ? null : $"{user.FirstName} {user.LastName}".Trim();
        }

        public async Task<object?> GetCurrentUserInfoAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var user = await _userService.GetUserByIdAsync(userId, cancellationToken);
            if (user == null)
            {
                return null;
            }

            return new
            {
                UserId = user.UserId,
                LastName = user.LastName ?? "",
                FirstName = user.FirstName ?? "",
                Login = user.Login ?? "",
                Account = user.Account != null
                    ? new { Avatar = user.Account.Avatar }
                    : null
            };
        }

        public async Task UpdateProfileAsync(
            User user,
            UpdateUserProfileRequest request,
            string? avatarUrl,
            CancellationToken cancellationToken = default)
        {
            await _userService.UpdateProfileAsync(
                user.UserId,
                request,
                avatarUrl,
                cancellationToken);

            var updatedUser = await _userService.GetUserByIdAsync(
                user.UserId,
                cancellationToken);
            var displayName = $"{updatedUser.FirstName} {updatedUser.LastName}".Trim();

            await SendProfileToPeersAsync(user.UserId, "ProfileUpdated", new
            {
                UserId = user.UserId.ToString(),
                AvatarUrl = updatedUser.Account?.Avatar,
                DisplayName = displayName
            }, cancellationToken);
        }

        public async Task<string> UploadAvatarAsync(
            User user,
            IFormFile avatarFile,
            CancellationToken cancellationToken = default)
        {
            var avatarUrl = await _userService.UploadAvatarAsync(
                user.UserId,
                avatarFile,
                cancellationToken);

            await SendProfileToPeersAsync(user.UserId, "AvatarUpdated", new
            {
                UserId = user.UserId.ToString(),
                AvatarUrl = avatarUrl
            }, cancellationToken);

            return avatarUrl;
        }

        public Task AssignRoleAsync(
            Guid userId,
            Guid roleId,
            CancellationToken cancellationToken = default)
        {
            return _userService.AssignRoleAsync(userId, roleId, cancellationToken);
        }

        public async Task DeleteAvatarAsync(
            User user,
            CancellationToken cancellationToken = default)
        {
            await _userService.DeleteAvatarAsync(user.UserId, cancellationToken);

            await SendProfileToPeersAsync(user.UserId, "AvatarUpdated", new
            {
                UserId = user.UserId.ToString(),
                AvatarUrl = (string?)null
            }, cancellationToken);
        }

        private async Task SendProfileToPeersAsync(
            Guid userId,
            string method,
            object payload,
            CancellationToken cancellationToken)
        {
            var peerIds = await _chatService.GetPeerUserIdsAsync(userId, cancellationToken);
            var groups = peerIds.Select(id => $"User_{id}").ToList();

            if (groups.Count == 0)
            {
                await _hubContext.Clients.Group($"User_{userId}")
                    .SendAsync(method, payload, cancellationToken);
                return;
            }

            await _hubContext.Clients.Groups(groups)
                .SendAsync(method, payload, cancellationToken);
        }
    }
}
