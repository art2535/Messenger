namespace Messenger.Core.Interfaces;

public interface IScheduledMessageClaimService
{
    Task<Guid?> TryClaimAsync(
        Guid scheduledMessageId,
        Guid candidateMessageId,
        DateTime nowUtc,
        CancellationToken ct = default);
}
