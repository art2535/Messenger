using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Messenger.Core.Hubs
{
    [Authorize]
    public class UserStatusHub : Hub
    {
        private readonly IUserStatusService _userStatusService;

        public UserStatusHub(IUserStatusService userStatusService)
        {
            _userStatusService = userStatusService;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var status = new UserStatus
            {
                UserId = userId,
                Online = true,
                LastActivity = DateTime.UtcNow
            };

            var cancellationToken = Context.GetHttpContext()?.RequestAborted ?? CancellationToken.None;

            await _userStatusService.UpdateUserStatusAsync(status, cancellationToken);
            await Clients.All.SendAsync("UserOnline", userId);

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var status = new UserStatus
            {
                UserId = userId,
                Online = false,
                LastActivity = DateTime.UtcNow
            };

            var cancellationToken = Context.GetHttpContext()?.RequestAborted ?? CancellationToken.None;

            await _userStatusService.UpdateUserStatusAsync(status, cancellationToken);
            await Clients.All.SendAsync("UserOffline", userId);

            await base.OnDisconnectedAsync(exception);
        }

        public async Task UpdateActivity()
        {
            var userId = Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var cancellationToken = Context.GetHttpContext()?.RequestAborted ?? CancellationToken.None;

            var status = await _userStatusService.GetUserStatusByUserIdAsync(userId, cancellationToken);
            if (status != null)
            {
                status.LastActivity = DateTime.UtcNow;
                await _userStatusService.UpdateUserStatusAsync(status, cancellationToken);
            }
        }
    }
}
