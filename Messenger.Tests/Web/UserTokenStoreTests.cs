using FluentAssertions;
using Messenger.Web.Services;

namespace Messenger.Tests.Web
{
    public class UserTokenStoreTests
    {
        [Fact]
        public void GetOrAdd_ShouldCreateEntryOnce()
        {
            var store = new UserTokenStore();
            var key = "session-1";

            var e1 = store.GetOrAdd(key, "access-1", "refresh-1");
            var e2 = store.GetOrAdd(key, "access-2", "refresh-2");

            e1.Should().BeSameAs(e2);
            e1.Tokens.AccessToken.Should().Be("access-1");
            e1.Key.Should().Be(key);
            e1.IsRevoked.Should().BeFalse();
        }

        [Fact]
        public void Remove_ShouldDropEntry()
        {
            var store = new UserTokenStore();
            store.GetOrAdd("k1", "a", "r");
            store.Remove("k1");

            store.Snapshot().Should().BeEmpty();
        }

        [Fact]
        public void RevokeSession_ByKey_ShouldRemoveAndMarkRevoked()
        {
            var store = new UserTokenStore();
            store.GetOrAdd("k1", "acc", "ref");

            var revoked = store.RevokeSession("k1", null, null);

            revoked.Should().HaveCount(1);
            revoked[0].Key.Should().Be("k1");
            store.Snapshot().Should().BeEmpty();
            store.IsRevoked("k1", null).Should().BeTrue();
        }

        [Fact]
        public void RevokeSession_ByAccessToken_ShouldMatchAndRemove()
        {
            var store = new UserTokenStore();
            store.GetOrAdd("k1", "access-token-xyz", "refresh-abc");
            store.GetOrAdd("k2", "other", "other-ref");

            var revoked = store.RevokeSession(null, "access-token-xyz", null);

            revoked.Should().HaveCount(1);
            revoked[0].Key.Should().Be("k1");
            store.Snapshot().Should().HaveCount(1);
            store.Snapshot()[0].Key.Should().Be("k2");
        }

        [Fact]
        public void RevokeSession_ByRefreshToken_ShouldMatchAndRemove()
        {
            var store = new UserTokenStore();
            store.GetOrAdd("k1", "a1", "refresh-special");

            var revoked = store.RevokeSession(null, null, "refresh-special");

            revoked.Should().HaveCount(1);
            store.IsRevoked(null, "refresh-special").Should().BeTrue();
        }

        [Fact]
        public void MarkRevoked_AndIsRevoked_WorkForKeyAndRefresh()
        {
            var store = new UserTokenStore();
            store.MarkRevoked("session-x", "refresh-y");

            store.IsRevoked("session-x", null).Should().BeTrue();
            store.IsRevoked(null, "refresh-y").Should().BeTrue();
            store.IsRevoked("other", "other-ref").Should().BeFalse();
        }

        [Fact]
        public void Snapshot_ShouldReturnCurrentEntries()
        {
            var store = new UserTokenStore();
            store.GetOrAdd("a", "1", "r1");
            store.GetOrAdd("b", "2", "r2");

            store.Snapshot().Should().HaveCount(2);
        }

        [Fact]
        public void TokenSet_Create_WithInvalidJwt_UsesFallbackLifetime()
        {
            var before = DateTime.UtcNow;
            var set = TokenSet.Create("not-a-jwt", "refresh", TimeSpan.FromMinutes(5));
            var after = DateTime.UtcNow;

            set.AccessToken.Should().Be("not-a-jwt");
            set.RefreshToken.Should().Be("refresh");
            set.ExpiresAtUtc.Should().BeOnOrAfter(before.AddMinutes(5).AddSeconds(-1));
            set.ExpiresAtUtc.Should().BeOnOrBefore(after.AddMinutes(5).AddSeconds(1));
        }
    }
}
