using Messenger.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Messenger.Tests.Infrastructure;

[Collection(RedisCollection.Name)]
public sealed class RedisIntegrationTests(RedisFixture fixture)
{

    [Fact]
    public async Task RateLimiter_SharesFixedWindowAcrossInstances()
    {
        var firstInstance = new RedisRateLimitService(fixture.Connection, NullLogger<RedisRateLimitService>.Instance);
        var secondInstance = new RedisRateLimitService(fixture.Connection, NullLogger<RedisRateLimitService>.Instance);
        var partition = "integration:shared:" + Guid.NewGuid().ToString("N");

        Assert.True(await firstInstance.TryAcquireAsync(partition, 3, TimeSpan.FromSeconds(2)));
        Assert.True(await secondInstance.TryAcquireAsync(partition, 3, TimeSpan.FromSeconds(2)));
        Assert.True(await firstInstance.TryAcquireAsync(partition, 3, TimeSpan.FromSeconds(2)));
        Assert.False(await secondInstance.TryAcquireAsync(partition, 3, TimeSpan.FromSeconds(2)));

        await Task.Delay(TimeSpan.FromMilliseconds(2200));

        Assert.True(await secondInstance.TryAcquireAsync(partition, 3, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task RateLimiter_IsolatesPartitions()
    {
        var limiter = new RedisRateLimitService(fixture.Connection, NullLogger<RedisRateLimitService>.Instance);
        var firstPartition = "integration:first:" + Guid.NewGuid().ToString("N");
        var secondPartition = "integration:second:" + Guid.NewGuid().ToString("N");

        Assert.True(await limiter.TryAcquireAsync(firstPartition, 1, TimeSpan.FromMinutes(1)));
        Assert.False(await limiter.TryAcquireAsync(firstPartition, 1, TimeSpan.FromMinutes(1)));
        Assert.True(await limiter.TryAcquireAsync(secondPartition, 1, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task CacheService_RoundTripsValues_AndExpiresKeys()
    {
        var cache = new RedisCacheService(fixture.Connection, NullLogger<RedisCacheService>.Instance);
        var key = $"redis-integration:{Guid.NewGuid():N}";
        var value = new CachePayload("integration", 42);

        Assert.True(cache.IsEnabled);

        await cache.SetAsync(key, value, TimeSpan.FromMilliseconds(250));

        var actual = await cache.GetAsync<CachePayload>(key);
        Assert.Equal(value, actual);

        await Task.Delay(TimeSpan.FromMilliseconds(400));
        Assert.Null(await cache.GetAsync<CachePayload>(key));
    }

    [Fact]
    public async Task CacheService_RemovesSingleAndMultipleKeys()
    {
        var cache = new RedisCacheService(fixture.Connection, NullLogger<RedisCacheService>.Instance);
        var first = $"redis-integration:{Guid.NewGuid():N}";
        var second = $"redis-integration:{Guid.NewGuid():N}";
        var third = $"redis-integration:{Guid.NewGuid():N}";

        await cache.SetAsync(first, "one", TimeSpan.FromMinutes(1));
        await cache.SetAsync(second, "two", TimeSpan.FromMinutes(1));
        await cache.SetAsync(third, "three", TimeSpan.FromMinutes(1));

        await cache.RemoveAsync(first);
        await cache.RemoveAsync(new[] { second, third, second, "" });

        Assert.Null(await cache.GetAsync<string>(first));
        Assert.Null(await cache.GetAsync<string>(second));
        Assert.Null(await cache.GetAsync<string>(third));
    }

    [Fact]
    public async Task DistributedLock_OnlyOneInstanceCanAcquireSameKey()
    {
        var firstInstance = CreateLock();
        var secondInstance = CreateLock();
        var key = $"redis-lock:{Guid.NewGuid():N}";

        var attempts = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(index =>
                (index % 2 == 0 ? firstInstance : secondInstance)
                    .TryAcquireAsync(key, TimeSpan.FromSeconds(10))));

        var acquired = attempts.Where(lease => lease is not null).ToArray();
        Assert.Single(acquired);

        foreach (var lease in acquired)
            await lease!.DisposeAsync();

        await using var afterRelease = await firstInstance.TryAcquireAsync(key, TimeSpan.FromSeconds(10));
        Assert.NotNull(afterRelease);
    }

    [Fact]
    public async Task DistributedLock_ExpiredOwnerCannotReleaseNewOwnersLease()
    {
        var firstInstance = CreateLock();
        var secondInstance = CreateLock();
        var key = $"redis-lock:{Guid.NewGuid():N}";

        var firstLease = await firstInstance.TryAcquireAsync(key, TimeSpan.FromMilliseconds(150));
        Assert.NotNull(firstLease);

        await Task.Delay(TimeSpan.FromMilliseconds(300));

        var secondLease = await secondInstance.TryAcquireAsync(key, TimeSpan.FromSeconds(10));
        Assert.NotNull(secondLease);

        await firstLease!.DisposeAsync();

        var competingLease = await firstInstance.TryAcquireAsync(key, TimeSpan.FromSeconds(10));
        Assert.Null(competingLease);

        await secondLease!.DisposeAsync();
        await using var finalLease = await firstInstance.TryAcquireAsync(key, TimeSpan.FromSeconds(10));
        Assert.NotNull(finalLease);
    }

    [Fact]
    public async Task PresenceTracker_TracksConnectionsAcrossInstances()
    {
        var firstInstance = CreatePresenceTracker();
        var secondInstance = CreatePresenceTracker();
        var userId = Guid.NewGuid();

        Assert.True(await firstInstance.ConnectAsync(userId, "node-a-connection"));
        Assert.False(await secondInstance.ConnectAsync(userId, "node-b-connection"));

        Assert.True(await firstInstance.HasActiveConnectionsAsync(userId));
        Assert.Equal(
            new[] { "node-a-connection", "node-b-connection" },
            (await secondInstance.GetActiveConnectionIdsAsync(userId)).OrderBy(value => value).ToArray());

        Assert.False(await firstInstance.DisconnectAsync(userId, "node-a-connection"));
        Assert.True(await secondInstance.HasActiveConnectionsAsync(userId));

        Assert.True(await secondInstance.DisconnectAsync(userId, "node-b-connection"));
        Assert.False(await firstInstance.HasActiveConnectionsAsync(userId));
    }

    [Fact]
    public async Task PresenceTracker_HeartbeatRenewsLocalConnectionLease()
    {
        var tracker = CreatePresenceTracker();
        var userId = Guid.NewGuid();
        const string connectionId = "heartbeat-connection";
        var key = $"messenger:presence:{userId:N}";

        Assert.True(await tracker.ConnectAsync(userId, connectionId));

        // Имитируем lease, который уже истёк в Redis, но ещё известен этому экземпляру API.
        await fixture.Database.SortedSetAddAsync(key, connectionId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1);
        Assert.False(await tracker.HasActiveConnectionsAsync(userId));

        await tracker.HeartbeatAsync();

        Assert.True(await tracker.HasActiveConnectionsAsync(userId));
        Assert.Equal(new[] { connectionId }, await tracker.GetActiveConnectionIdsAsync(userId));
    }

    [Fact]
    public async Task PresenceTracker_ExpiredLeaseIsRemovedFromSharedPresence()
    {
        var tracker = CreatePresenceTracker();
        var userId = Guid.NewGuid();
        var key = $"messenger:presence:{userId:N}";

        await fixture.Database.SortedSetAddAsync(
            key,
            "expired-connection",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1);

        Assert.False(await tracker.HasActiveConnectionsAsync(userId));
        Assert.Empty(await tracker.GetActiveConnectionIdsAsync(userId));
    }

    [Fact]
    public async Task PresenceTracker_ConcurrentConnectionsProduceSingleOnlineAndOfflineTransitions()
    {
        var tracker = CreatePresenceTracker();
        var userId = Guid.NewGuid();
        var connectionIds = Enumerable.Range(0, 12)
            .Select(index => $"parallel-connection-{index}")
            .ToArray();

        var onlineTransitions = await Task.WhenAll(connectionIds.Select(connectionId =>
            tracker.ConnectAsync(userId, connectionId)));

        Assert.Single(onlineTransitions.Where(becameOnline => becameOnline));
        Assert.Equal(
            connectionIds.OrderBy(value => value),
            (await tracker.GetActiveConnectionIdsAsync(userId)).OrderBy(value => value));

        var offlineTransitions = await Task.WhenAll(connectionIds.Select(connectionId =>
            tracker.DisconnectAsync(userId, connectionId)));

        Assert.Single(offlineTransitions.Where(becameOffline => becameOffline));
        Assert.False(await tracker.HasActiveConnectionsAsync(userId));
    }

    [Fact]
    public async Task PresenceTracker_HeartbeatCannotResurrectDisconnectedConnection()
    {
        var tracker = CreatePresenceTracker();
        var userId = Guid.NewGuid();
        const string connectionId = "disconnect-heartbeat-race";

        Assert.True(await tracker.ConnectAsync(userId, connectionId));

        var heartbeatTasks = Enumerable.Range(0, 20)
            .Select(_ => tracker.HeartbeatAsync())
            .ToArray();
        var disconnectTask = tracker.DisconnectAsync(userId, connectionId);

        await Task.WhenAll(heartbeatTasks.Append(disconnectTask));

        Assert.False(await tracker.HasActiveConnectionsAsync(userId));
        Assert.Empty(await tracker.GetActiveConnectionIdsAsync(userId));
    }

    private RedisDistributedLock CreateLock() =>
        new(fixture.Connection, NullLogger<RedisDistributedLock>.Instance);

    private RedisUserPresenceTracker CreatePresenceTracker() =>
        new(fixture.Connection, NullLogger<RedisUserPresenceTracker>.Instance);

    public sealed record CachePayload(string Name, int Count);
}

public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public IConnectionMultiplexer Connection { get; private set; } = null!;
    public IDatabase Database => Connection.GetDatabase();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Connection = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        Connection.Dispose();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("Redis integration")]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>
{
    public const string Name = "Redis integration";
}
