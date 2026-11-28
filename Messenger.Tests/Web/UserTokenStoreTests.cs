using System.Reflection;
using Messenger.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Messenger.Tests.Web;

public sealed class UserTokenStoreTests
{
    [Fact]
    public void GetOrAdd_ReturnsOneEntryForConcurrentRequests()
    {
        var store = new UserTokenStore();
        var entries = Enumerable.Range(0, 20)
            .AsParallel()
            .Select(_ => store.GetOrAdd("same-session", "access", "refresh"))
            .ToArray();

        Assert.All(entries, entry => Assert.Same(entries[0], entry));
        Assert.Single(store.Snapshot());
    }

    [Fact]
    public void RemoveIfCurrent_DoesNotRemoveReplacementEntry()
    {
        var store = new UserTokenStore();
        var stale = store.GetOrAdd("session", "old-access", "old-refresh");
        store.Remove("session");
        var replacement = store.GetOrAdd("session", "new-access", "new-refresh");

        Assert.False(store.RemoveIfCurrent("session", stale));
        Assert.Same(replacement, Assert.Single(store.Snapshot()));
    }

    [Fact]
    public void RevokeSession_RemovesAllEntriesSharingRefreshTokenAndCreatesTombstones()
    {
        var store = new UserTokenStore();
        var first = store.GetOrAdd("session-a", "access-a", "shared-refresh");
        var second = store.GetOrAdd("session-b", "access-b", "shared-refresh");

        var revoked = store.RevokeSession("session-a", "access-a", "shared-refresh");

        Assert.Contains(first, revoked);
        Assert.Contains(second, revoked);
        Assert.Empty(store.Snapshot());
        Assert.True(first.IsRevoked);
        Assert.True(second.IsRevoked);
        Assert.True(store.IsRevoked("session-a", "shared-refresh"));
        Assert.True(store.IsRevoked("session-b", "shared-refresh"));
    }

    [Fact]
    public async Task TokenRefreshBackgroundService_RefreshCycleHandlesEmptyStore()
    {
        var store = new UserTokenStore();
        var factory = new Mock<IHttpClientFactory>();
        var refresher = new TokenRefresher(
            factory.Object,
            new ConfigurationBuilder().Build(),
            Options.Create(new TokenRefreshOptions()),
            store,
            NullLogger<TokenRefresher>.Instance);
        using var provider = new ServiceCollection().BuildServiceProvider();
        var service = new TokenRefreshBackgroundService(
            store,
            refresher,
            Options.Create(new TokenRefreshOptions()),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TokenRefreshBackgroundService>.Instance);

        var method = typeof(TokenRefreshBackgroundService).GetMethod(
            "RefreshAllAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        await (Task)method!.Invoke(service, new object[] { CancellationToken.None })!;
    }
}
