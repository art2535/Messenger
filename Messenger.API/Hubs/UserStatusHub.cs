using Messenger.Realtime.Metrics;
using Messenger.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Messenger.API.Hubs
{
    [Authorize]
    public sealed class UserStatusHub : Hub
    {
        private readonly IUserStatusService _userStatusService;
        private readonly IChatService _chatService;
        private readonly IUserService _userService;
        private readonly IUserPresenceTracker _presenceTracker;
        private readonly IAccountSettingsService _accountSettingsService;

        public UserStatusHub(
            IUserStatusService userStatusService,
            IChatService chatService,
            IUserService userService,
            IUserPresenceTracker presenceTracker,
            IAccountSettingsService accountSettingsService)
        {
            _userStatusService = userStatusService;
            _chatService = chatService;
            _userService = userService;
            _presenceTracker = presenceTracker;
            _accountSettingsService = accountSettingsService;
        }

        private async Task SendPresenceToPeersAsync(Guid userId, string method, object payload, CancellationToken cancellationToken)
        {
            var peerIds = await _chatService.GetPeerUserIdsAsync(userId, cancellationToken);
            var groups = peerIds.Select(id => $"User_{id}").ToList();

            if (groups.Count > 0)
                await Clients.Groups(groups).SendAsync(method, payload, cancellationToken);
            else
                await Clients.Group($"User_{userId}").SendAsync(method, payload, cancellationToken);
        }

        public override async Task OnConnectedAsync()
        {
            var userId = await ResolveCurrentUserIdAsync(Context.ConnectionAborted);
            Context.Items["PresenceUserId"] = userId;

            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                $"User_{userId}",
                Context.ConnectionAborted);

            var becameOnline = await _presenceTracker.ConnectAsync(
                userId,
                Context.ConnectionId,
                Context.ConnectionAborted);

            if (becameOnline)
            {
                await _userStatusService.UpdateUserStatusAsync(
                    userId,
                    true,
                    Context.ConnectionAborted);

                var showOnline = true;
                try
                {
                    var settings = await _accountSettingsService.GetByUserIdAsync(
                        userId,
                        Context.ConnectionAborted);
                    showOnline = settings.ShowOnline;
                }
                catch (OperationCanceledException) when (Context.ConnectionAborted.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // При недоступности настроек сохраняем прежнее поведение: online по умолчанию.
                }

                if (showOnline)
                {
                    await SendPresenceToPeersAsync(
                        userId,
                        "UserOnline",
                        userId,
                        Context.ConnectionAborted);
                }
            }

            MessengerMetrics.SignalRActiveConnections.Add(
                1,
                new KeyValuePair<string, object?>("hub", "userstatus"));

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (Context.Items.TryGetValue("PresenceUserId", out var value) && value is Guid userId)
            {
                var becameOffline = await _presenceTracker.DisconnectAsync(
                    userId,
                    Context.ConnectionId);

                if (becameOffline)
                {
                    await _userStatusService.UpdateUserStatusAsync(userId, false);

                    await SendPresenceToPeersAsync(
                        userId,
                        "UserOffline",
                        userId,
                        CancellationToken.None);
                }

                MessengerMetrics.SignalRActiveConnections.Add(
                    -1,
                    new KeyValuePair<string, object?>("hub", "userstatus"));
            }

            if (exception != null)
            {
                MessengerMetrics.SignalRErrors.Add(
                    1,
                    new KeyValuePair<string, object?>("hub", "userstatus"),
                    new KeyValuePair<string, object?>("error_type", exception.GetType().Name));
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task UpdateActivity()
        {
            if (!Context.Items.TryGetValue("PresenceUserId", out var value) || value is not Guid userId)
                throw new HubException("Пользователь не определён.");

            await _userStatusService.UpdateActivityAsync(
                userId,
                Context.ConnectionAborted);
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
