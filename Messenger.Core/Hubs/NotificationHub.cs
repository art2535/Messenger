using Messenger.Core.Interfaces;
using Messenger.Core.Metrics;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Messenger.Core.Hubs
{
    [Authorize]
    public class NotificationHub : Hub
    {
        private readonly INotificationService _notificationService;

        public NotificationHub(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        public override async Task OnConnectedAsync()
        {
            MessengerMetrics.SignalRActiveConnections.Add(1, 
                new KeyValuePair<string, object?>("hub", "notification"));

            var userId = Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Notifications_{userId}");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            MessengerMetrics.SignalRActiveConnections.Add(-1, 
                new KeyValuePair<string, object?>("hub", "notification"));

            if (exception != null)
            {
                MessengerMetrics.SignalRErrors.Add(1, 
                    new KeyValuePair<string, object?>("hub", "notification"),
                    new KeyValuePair<string, object?>("error_type", exception.GetType().Name));
            }

            var userId = Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Notifications_{userId}");
            await base.OnDisconnectedAsync(exception);
        }

        public async Task SendNotification(Guid userId, string text)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                var notification = new Notification
                {
                    NotificationId = Guid.NewGuid(),
                    UserId = userId,
                    Text = text,
                    CreationDate = DateTime.UtcNow,
                    Read = false
                };

                var cancellationToken = Context.GetHttpContext()?.RequestAborted ?? CancellationToken.None;
                await _notificationService.CreateNotificationAsync(userId, text, cancellationToken);
                await Clients.Group($"Notifications_{userId}").SendAsync("ReceiveNotification", notification);
                
                MessengerMetrics.NotificationsSent.Add(1);
            }
            finally
            {
                var duration = (DateTime.UtcNow - startTime).TotalSeconds;
                MessengerMetrics.HubActionDuration.Record(duration, 
                    new KeyValuePair<string, object?>("action", "send_notification"));
            }
        }
    }
}
