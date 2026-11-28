using Messenger.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Messenger.Tests.Realtime.RateLimiting;

public sealed class RateLimiterServiceTests
{
    [Fact]
    public async Task LocalFallback_EnforcesLimitAndSeparatesPartitions()
    {
        var limiter = new RedisRateLimitService(null, NullLogger<RedisRateLimitService>.Instance);

        for (var i = 0; i < 3; i++)
            Assert.True(await limiter.TryAcquireAsync("typing:user-a", 3, TimeSpan.FromMinutes(1)));

        Assert.False(await limiter.TryAcquireAsync("typing:user-a", 3, TimeSpan.FromMinutes(1)));
        Assert.True(await limiter.TryAcquireAsync("typing:user-b", 3, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task LocalFallback_ValidatesArguments()
    {
        var limiter = new RedisRateLimitService(null, NullLogger<RedisRateLimitService>.Instance);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await limiter.TryAcquireAsync("key", 0, TimeSpan.FromMinutes(1)));
    }
}
