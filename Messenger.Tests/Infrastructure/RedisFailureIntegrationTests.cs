using Messenger.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Messenger.Tests.Infrastructure;

[Collection(RedisCollection.Name)]
public sealed class RedisFailureIntegrationTests(RedisFixture fixture)
{
    [Fact]
    public async Task RateLimiter_FailsClosedDuringRedisOutage_AndRecoversAfterRedisReturns()
    {
        var limiter = new RedisRateLimitService(
            fixture.Connection,
            NullLogger<RedisRateLimitService>.Instance);
        var partition = "redis-recovery:" + Guid.NewGuid().ToString("N");

        await fixture.PauseAsync();
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            Assert.False(await limiter.TryAcquireAsync(partition, 2, TimeSpan.FromMinutes(1)));
        }
        finally
        {
            await fixture.ResumeAsync();
        }

        await WaitForRedisAsync(fixture.Connection);

        Assert.True(await limiter.TryAcquireAsync(partition, 2, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task PresenceHeartbeat_RenewsLocalConnectionsAfterRedisRecovers()
    {
        var tracker = new RedisUserPresenceTracker(
            fixture.Connection,
            NullLogger<RedisUserPresenceTracker>.Instance);
        var userId = Guid.NewGuid();
        const string connectionId = "redis-recovery-presence";

        Assert.True(await tracker.ConnectAsync(userId, connectionId));

        await fixture.PauseAsync();
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            // Redis may report an outage as RedisException or as a timeout exception.
            var outageException = await Record.ExceptionAsync(() => tracker.HeartbeatAsync());
            Assert.NotNull(outageException);
        }
        finally
        {
            await fixture.ResumeAsync();
        }

        await WaitForRedisAsync(fixture.Connection);
        await tracker.HeartbeatAsync();

        Assert.True(await tracker.HasActiveConnectionsAsync(userId));
        Assert.Equal(new[] { connectionId }, await tracker.GetActiveConnectionIdsAsync(userId));
    }

    private static async Task WaitForRedisAsync(IConnectionMultiplexer connection)
    {
        var timeout = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < timeout)
        {
            try
            {
                await connection.GetDatabase().PingAsync();
                return;
            }
            catch (RedisException)
            {
                await Task.Delay(250);
            }
        }

        throw new TimeoutException("Redis не восстановил соединение за 20 секунд.");
    }
}
