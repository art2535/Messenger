namespace Messenger.Application.Interfaces;

/// <summary>
/// Provides per-key fixed-window rate limiting shared by API and realtime transports.
/// </summary>
public interface IRateLimitService
{
    ValueTask<bool> TryAcquireAsync(
        string partitionKey,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken = default);
}
