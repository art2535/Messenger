namespace Messenger.Core.Interfaces;

public interface IScheduledMessageClaimService
{
    Task<bool> TryClaimAsync(
        Guid scheduledMessageId,
        DateTime nowUtc,
        CancellationToken ct = default);
}
