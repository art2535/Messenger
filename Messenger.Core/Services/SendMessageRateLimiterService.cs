using System.Threading.RateLimiting;

namespace Messenger.Core.Services
{
    public class SendMessageRateLimiterService
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

        public async ValueTask<bool> TryAcquireAsync(string userId)
        {
            using var lease = await _limiter.AcquireAsync(userId);
            return lease.IsAcquired;
        }
    }
}
