using Messenger.Core.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.Infrastructure.Services.MessageRealtimeNotifier.cs
{
    public sealed class MessageRealtimeNotifier : IMessageRealtimeNotifier
    {
        private readonly IHubContext<ChatHub> _hubContext;

        public MessageRealtimeNotifier(IHubContext<ChatHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public Task SendToChatAsync(
            Guid chatId,
            string eventName,
            object payload,
            CancellationToken cancellationToken = default)
        {
            return _hubContext.Clients.Group(chatId.ToString())
                .SendAsync(eventName, payload, cancellationToken);
        }

        public async Task SendToUsersAsync(
            IEnumerable<Guid> userIds,
            string eventName,
            object payload,
            CancellationToken cancellationToken = default)
        {
            foreach (var userId in userIds)
            {
                await _hubContext.Clients.Group($"User_{userId}")
                    .SendAsync(eventName, payload, cancellationToken);
            }
        }

        public async Task SendToChatAndUsersAsync(
            Guid chatId,
            IEnumerable<Guid> userIds,
            string eventName,
            object payload,
            CancellationToken cancellationToken = default)
        {
            await SendToChatAsync(chatId, eventName, payload, cancellationToken);
            await SendToUsersAsync(userIds, eventName, payload, cancellationToken);
        }

        public async Task SendMessagePublishedAsync(
            Guid chatId,
            object message,
            Guid messageId,
            IEnumerable<Guid> userIds,
            CancellationToken cancellationToken = default)
        {
            await SendToChatAsync(chatId, "ReceiveMessage", message, cancellationToken);
            await SendToChatAsync(
                chatId,
                "MessageSendingStatus",
                new
                {
                    MessageId = messageId,
                    ChatId = chatId,
                    Status = "Sent",
                    Timestamp = DateTimeOffset.UtcNow
                },
                cancellationToken);
            await SendToUsersAsync(userIds, "ReceiveMessage", message, cancellationToken);
        }
    }
}