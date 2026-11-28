using Messenger.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Messenger.Tests.Infrastructure;

public sealed class RedisFallbackServiceTests
{
    [Fact]
    public async Task PresenceTracker_LocalFallback_TracksMultipleConnectionsAndTransitions()
    {
        var tracker = new RedisUserPresenceTracker(null, NullLogger<RedisUserPresenceTracker>.Instance);
        var userId = Guid.NewGuid();

        Assert.True(await tracker.ConnectAsync(userId, "connection-1"));
        Assert.False(await tracker.ConnectAsync(userId, "connection-1"));
        Assert.False(await tracker.ConnectAsync(userId, "connection-2"));
        Assert.True(await tracker.HasActiveConnectionsAsync(userId));
        Assert.Equal(new[] { "connection-1", "connection-2" },
            (await tracker.GetActiveConnectionIdsAsync(userId)).OrderBy(x => x).ToArray());

        Assert.False(await tracker.DisconnectAsync(userId, "connection-1"));
        Assert.True(await tracker.DisconnectAsync(userId, "connection-2"));
        Assert.False(await tracker.HasActiveConnectionsAsync(userId));
        Assert.Empty(await tracker.GetActiveConnectionIdsAsync(userId));
    }

    [Fact]
    public async Task PresenceTracker_DisconnectUsesTrackedOwner_NotCallerSuppliedUserId()
    {
        var tracker = new RedisUserPresenceTracker(null, NullLogger<RedisUserPresenceTracker>.Instance);
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        Assert.True(await tracker.ConnectAsync(ownerId, "owned-connection"));
        Assert.True(await tracker.DisconnectAsync(otherId, "owned-connection"));
        Assert.False(await tracker.HasActiveConnectionsAsync(ownerId));
        Assert.False(await tracker.HasActiveConnectionsAsync(otherId));
    }

    [Fact]
    public async Task PresenceTracker_PropagatesCancellationBeforeChangingState()
    {
        var tracker = new RedisUserPresenceTracker(null, NullLogger<RedisUserPresenceTracker>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            tracker.ConnectAsync(Guid.NewGuid(), "cancelled", cancellation.Token));
    }

    [Fact]
    public async Task PresenceTracker_HeartbeatIsSafeWithoutRedis()
    {
        var tracker = new RedisUserPresenceTracker(null, NullLogger<RedisUserPresenceTracker>.Instance);
        await tracker.ConnectAsync(Guid.NewGuid(), "local-connection");
        await tracker.HeartbeatAsync();
    }

    [Fact]
    public async Task CacheService_DisabledRedisFallsBackToMissAndHonorsCancellation()
    {
        var cache = new RedisCacheService(null, NullLogger<RedisCacheService>.Instance);
        Assert.False(cache.IsEnabled);
        Assert.Null(await cache.GetAsync<string>("missing"));
        await cache.SetAsync("key", "value", TimeSpan.FromMinutes(1));
        await cache.RemoveAsync("key");
        await cache.RemoveAsync(new[] { "key", "", "key" });

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetAsync<string>("cancelled", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.SetAsync("cancelled", "value", TimeSpan.FromMinutes(1), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.RemoveAsync("cancelled", cancellation.Token));
    }

    [Fact]
    public async Task DistributedLock_WithoutRedisReturnsNoOpLease_AndHonorsCancellation()
    {
        var distributedLock = new RedisDistributedLock(null, NullLogger<RedisDistributedLock>.Instance);
        var lease = await distributedLock.TryAcquireAsync("test-lock", TimeSpan.FromSeconds(5));

        Assert.NotNull(lease);
        await lease.DisposeAsync();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            distributedLock.TryAcquireAsync("cancelled", TimeSpan.FromSeconds(5), cancellation.Token));
    }
}
