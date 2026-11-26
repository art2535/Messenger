using Messenger.Core.Interfaces;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Persistence;

public sealed class MessageNotificationOutboxRecoveryService(
    GuapMessengerContext context) : IMessageNotificationOutboxRecoveryService
{
    public async Task<int> RecoverDeadLettersAsync(
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 1000);

        return await context.MessageNotificationOutbox
            .Where(row => row.Status == "DeadLetter")
            .OrderBy(row => row.CreatedAt)
            .Take(take)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.Status, "Pending")
                    .SetProperty(row => row.AttemptCount, 0)
                    .SetProperty(row => row.NextAttemptAt, DateTime.UtcNow)
                    .SetProperty(row => row.LockedUntil, (DateTime?)null)
                    .SetProperty(row => row.DeliveredAt, (DateTime?)null)
                    .SetProperty(row => row.LastError, (string?)null),
                cancellationToken);
    }
}
