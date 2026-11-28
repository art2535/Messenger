using System.Net;
using Messenger.Core.Configuration;
using Messenger.Web.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Messenger.Tests.Shared.TestDoubles;

namespace Messenger.Tests.Web.Authentication;

public sealed class TokenRefresherTests
{
    [Fact]
    public async Task RefreshIfNeededAsync_ReplacesAccessAndRefreshTokensOnSuccess()
    {
        var store = new UserTokenStore();
        var entry = CreateExpiredEntry(store, "session-success");
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"access_token":"new-access","refresh_token":"new-refresh","expires_in":3600}""")
        }));
        var refresher = CreateRefresher(store, handler);

        Assert.True(refresher.NeedsRefresh(entry));
        await refresher.RefreshIfNeededAsync(entry, CancellationToken.None);

        Assert.Equal("new-access", entry.Tokens.AccessToken);
        Assert.Equal("new-refresh", entry.Tokens.RefreshToken);
        Assert.True(entry.Tokens.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(50));
        Assert.False(entry.IsRevoked);
    }

    [Fact]
    public async Task RefreshIfNeededAsync_RevokesSessionWhenSsoRejectsRefreshToken()
    {
        var store = new UserTokenStore();
        var entry = CreateExpiredEntry(store, "session-rejected");
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("invalid_grant")
        }));
        var refresher = CreateRefresher(store, handler);

        await refresher.RefreshIfNeededAsync(entry, CancellationToken.None);

        Assert.True(entry.IsRevoked);
        Assert.Empty(store.Snapshot());
        Assert.True(store.IsRevoked(entry.Key, "old-refresh"));
    }

    [Fact]
    public async Task RefreshIfNeededAsync_DoesNotCallSsoForRevokedTombstone()
    {
        var store = new UserTokenStore();
        var entry = CreateExpiredEntry(store, "session-tombstone");
        store.MarkRevoked(entry.Key, entry.Tokens.RefreshToken);
        var calls = 0;
        var handler = new StubHandler((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var refresher = CreateRefresher(store, handler);

        await refresher.RefreshIfNeededAsync(entry, CancellationToken.None);

        Assert.True(entry.IsRevoked);
        Assert.Empty(store.Snapshot());
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task RefreshIfNeededAsync_SerializesConcurrentRefreshesForSameEntry()
    {
        var store = new UserTokenStore();
        var entry = CreateExpiredEntry(store, "session-concurrent");
        var calls = 0;
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(20, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"rotated-access","refresh_token":"rotated-refresh","expires_in":3600}""")
            };
        });
        var refresher = CreateRefresher(store, handler);

        await Task.WhenAll(
            refresher.RefreshIfNeededAsync(entry, CancellationToken.None),
            refresher.RefreshIfNeededAsync(entry, CancellationToken.None));

        Assert.Equal(1, calls);
        Assert.Equal("rotated-access", entry.Tokens.AccessToken);
    }

    private static TokenEntry CreateExpiredEntry(UserTokenStore store, string key)
    {
        var entry = store.GetOrAdd(key, "old-access", "old-refresh");
        entry.Tokens = new TokenSet("old-access", "old-refresh", DateTime.UtcNow.AddSeconds(-10));
        return entry;
    }

    private static TokenRefresher CreateRefresher(UserTokenStore store, HttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        return new TokenRefresher(
            factory.Object,
            Options.Create(new IdentityProviderOptions
            {
                Instance = "https://sso.example.test/realms/",
                TenantId = "test",
                ClientId = "messenger-tests",
                ClientSecret = "test-secret"
            }),
            Options.Create(new TokenRefreshOptions { RefreshBeforeExpirySeconds = 90 }),
            store,
            NullLogger<TokenRefresher>.Instance);
    }
}
