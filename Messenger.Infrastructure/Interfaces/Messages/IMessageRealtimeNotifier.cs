using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Messenger.Infrastructure.Interfaces.Messages
{
    public interface IMessageRealtimeNotifier
    {
        Task SendToChatAsync(
            Guid chatId,
            string eventName,
            object payload,
            CancellationToken cancellationToken = default);

        Task SendToUsersAsync(
            IEnumerable<Guid> userIds,
            string eventName,
            object payload,
            CancellationToken cancellationToken = default);

        Task SendToChatAndUsersAsync(
            Guid chatId,
            IEnumerable<Guid> userIds,
            string eventName,
            object payload,
            CancellationToken cancellationToken = default);

        Task SendMessagePublishedAsync(
            Guid chatId,
            object message,
            Guid messageId,
            IEnumerable<Guid> userIds,
            CancellationToken cancellationToken = default);
    }
}