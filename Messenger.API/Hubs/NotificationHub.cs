using Messenger.Application.Interfaces;
using Messenger.Realtime.Metrics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Messenger.API.Hubs
{
    [Authorize]
    public sealed class NotificationHub : Hub
    {
        private readonly IUserService _userService;

        public NotificationHub(IUserService userService)
        {
            _userService = userService;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = await ResolveCurrentUserIdAsync(Context.ConnectionAborted);
            Context.Items["NotificationUserId"] = userId;

            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                $"Notifications_{userId}",
                Context.ConnectionAborted);

            MessengerMetrics.SignalRActiveConnections.Add(
                1,
                new KeyValuePair<string, object?>("hub", "notification"));

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (Context.Items.TryGetValue("NotificationUserId", out var value) && value is Guid userId)
            {
                await Groups.RemoveFromGroupAsync(
                    Context.ConnectionId,
                    $"Notifications_{userId}",
                    CancellationToken.None);

                MessengerMetrics.SignalRActiveConnections.Add(
                    -1,
                    new KeyValuePair<string, object?>("hub", "notification"));
            }

            if (exception != null)
            {
                MessengerMetrics.SignalRErrors.Add(
                    1,
                    new KeyValuePair<string, object?>("hub", "notification"),
                    new KeyValuePair<string, object?>("error_type", exception.GetType().Name));
            }

            await base.OnDisconnectedAsync(exception);
        }

        private async Task<Guid> ResolveCurrentUserIdAsync(CancellationToken cancellationToken)
        {
            var externalId = Context.User?.FindFirst("sub")?.Value
                ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrWhiteSpace(externalId))
                throw new HubException("Не удалось определить пользователя.");

            var user = await _userService.GetUserByExternalIdAsync(externalId, cancellationToken);
            return user?.UserId ?? throw new HubException("Пользователь не найден.");
        }
    }
}
