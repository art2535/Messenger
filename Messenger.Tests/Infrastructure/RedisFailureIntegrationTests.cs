using Messenger.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Messenger.Tests.Infrastructure;

public sealed class RedisFailureIntegrationTests
{
    [Fact]
    public async Task RateLimiter_FailsClosedWhenConfiguredRedisIsUnavailable()
    {
        var database = new Mock<IDatabase>();
        database.Setup(value => value.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(
                ConnectionFailureType.UnableToConnect,
                "Redis is unavailable"));

        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(value => value.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        var limiter = new RedisRateLimitService(
            redis.Object,
            NullLogger<RedisRateLimitService>.Instance);

        var acquired = await limiter.TryAcquireAsync(
            "failure-test:" + Guid.NewGuid().ToString("N"),
            100,
            TimeSpan.FromMinutes(1));

        Assert.False(acquired);
        database.Verify(value => value.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }
}
