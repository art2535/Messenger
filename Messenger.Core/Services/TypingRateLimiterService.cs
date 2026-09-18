using System.Threading.RateLimiting;

namespace Messenger.Core.Services
{
    public class TypingRateLimiterService
    {
        private readonly PartitionedRateLimiter<string> _limiter;

        public TypingRateLimiterService()
        {
            _limiter = PartitionedRateLimiter.Create<string, string>(userId =>
                RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 60,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        }

        public async ValueTask<bool> TryAcquireAsync(string userId)
        {
            using var lease = await _limiter.AcquireAsync(userId);
            return lease.IsAcquired;
        }
    }
}
