using System.Threading.RateLimiting;

namespace Messenger.Realtime.Services
{
    public sealed class SendMessageRateLimiterService : IDisposable
    {
        private readonly PartitionedRateLimiter<string> _limiter;

        public SendMessageRateLimiterService()
        {
            _limiter = PartitionedRateLimiter.Create<string, string>(userId =>
                RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 5,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                }));
        }

        public async ValueTask<bool> TryAcquireAsync(string userId, CancellationToken cancellationToken = default)
        {
            using var lease = await _limiter.AcquireAsync(userId, 1, cancellationToken);
            return lease.IsAcquired;
        }

        public void Dispose() => _limiter.Dispose();
    }
}
