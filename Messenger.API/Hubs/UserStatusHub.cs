using Messenger.Realtime.Metrics;
using Messenger.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Messenger.API.Hubs
{
    [Authorize]
    public class UserStatusHub : Hub
    {
        private readonly IUserStatusService _userStatusService;
        private readonly IChatService _chatService;
        private readonly IAccountSettingsService _accountSettingsService;

        public UserStatusHub(IUserStatusService userStatusService, IChatService chatService,
            IAccountSettingsService accountSettingsService)
        {
            _userStatusService = userStatusService;
            _chatService = chatService;
            _accountSettingsService = accountSettingsService;
        }

        private async Task SendPresenceToPeersAsync(Guid userId, string method, object payload, CancellationToken ct)
        {
            var peerIds = await _chatService.GetPeerUserIdsAsync(userId, ct);
            var groups = peerIds.Select(id => $"User_{id}").ToList();
            await Groups.AddToGroupAsync(Context.ConnectionId, $"User_{userId}");
            if (groups.Count > 0)
                await Clients.Groups(groups).SendAsync(method, payload, ct);
            else
                await Clients.Group($"User_{userId}").SendAsync(method, payload, ct);
        }

        public override async Task OnConnectedAsync()
        {
            MessengerMetrics.SignalRActiveConnections.Add(1,
                new KeyValuePair<string, object?>("hub", "userstatus"));

            var userId = Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var cancellationToken = Context.GetHttpContext()?.RequestAborted ?? CancellationToken.None;

            await _userStatusService.UpdateUserStatusAsync(userId, true, cancellationToken);

            var showOnline = true;
            try
            {
                var settings = await _accountSettingsService.GetByUserIdAsync(userId, cancellationToken);
                showOnline = settings.ShowOnline;
            }
            catch { }

            if (showOnline)
                await SendPresenceToPeersAsync(userId, "UserOnline", userId, cancellationToken);

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            MessengerMetrics.SignalRActiveConnections.Add(-1,
                new KeyValuePair<string, object?>("hub", "userstatus"));

            if (exception != null)
            {
                MessengerMetrics.SignalRErrors.Add(1,
                    new KeyValuePair<string, object?>("hub", "userstatus"),
                    new KeyValuePair<string, object?>("error_type", exception.GetType().Name));
            }

            var userId = Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var cancellationToken = Context.GetHttpContext()?.RequestAborted ?? CancellationToken.None;

            await _userStatusService.UpdateUserStatusAsync(userId, false, cancellationToken);
            await SendPresenceToPeersAsync(userId, "UserOffline", userId, cancellationToken);

            await base.OnDisconnectedAsync(exception);
        }

        public async Task UpdateActivity()
        {
            var userId = Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var cancellationToken = Context.GetHttpContext()?.RequestAborted ?? CancellationToken.None;

            await _userStatusService.UpdateActivityAsync(userId, cancellationToken);
        }
    }
}
