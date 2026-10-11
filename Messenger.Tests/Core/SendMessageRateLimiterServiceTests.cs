using Messenger.Core.Services;

namespace Messenger.Tests.Core
{
    public class SendMessageRateLimiterServiceTests
    {
        [Fact]
        public async Task TryAcquireAsync_AllowsWithinLimit()
        {
            var service = new SendMessageRateLimiterService();
            var key = $"allow-{Guid.NewGuid():N}";

            for (var i = 0; i < 30; i++)
            {
                Assert.True(
                    await service.TryAcquireAsync(key),
                    $"permit #{i + 1} should be acquired");
            }
        }

        [Fact]
        public async Task TryAcquireAsync_BlocksAboveLimit()
        {
            var service = new SendMessageRateLimiterService();
            var key = $"block-{Guid.NewGuid():N}";

            for (var i = 0; i < 30; i++)
                Assert.True(await service.TryAcquireAsync(key));

            var overLimit = await TryAcquireWithinAsync(service, key, TimeSpan.FromMilliseconds(200));
            Assert.False(overLimit, "31st permit for the same user must be denied");
        }

        [Fact]
        public async Task TryAcquireAsync_PartitionsByUser()
        {
            var service = new SendMessageRateLimiterService();
            var userA = $"part-a-{Guid.NewGuid():N}";
            var userB = $"part-b-{Guid.NewGuid():N}";

            for (var i = 0; i < 30; i++)
                Assert.True(await service.TryAcquireAsync(userA));

            var deniedA = await TryAcquireWithinAsync(service, userA, TimeSpan.FromMilliseconds(200));
            Assert.False(deniedA, "user-a must be rate-limited after 30 permits");

            Assert.True(
                await service.TryAcquireAsync(userB),
                "user-b must have an independent partition");
        }
        
        private static async Task<bool> TryAcquireWithinAsync(SendMessageRateLimiterService service,
            string key, TimeSpan timeout)
        {
            var acquire = service.TryAcquireAsync(key).AsTask();
            var completed = await Task.WhenAny(acquire, Task.Delay(timeout));

            if (completed != acquire)
                return false;

            return await acquire;
        }
    }
}