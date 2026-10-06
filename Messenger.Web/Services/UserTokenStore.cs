using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Messenger.Web.Services
{
    public sealed class UserTokenStore
    {
        public const string StoreKeyItem = ".Messenger.TokenStoreKey";

        private static readonly TimeSpan RevokedTtl = TimeSpan.FromHours(13);

        private readonly ConcurrentDictionary<string, TokenEntry> _entries = new();
        private readonly ConcurrentDictionary<string, DateTime> _revoked = new();

        public TokenEntry GetOrAdd(string key, string accessToken, string refreshToken)
            => _entries.GetOrAdd(key, k => new TokenEntry(
                k, TokenSet.Create(accessToken, refreshToken, TokenSet.DefaultLifetime)));

        public void Remove(string key) => _entries.TryRemove(key, out _);

        public IReadOnlyList<TokenEntry> RevokeSession(string? key, string? accessToken, string? refreshToken)
        {
            var revoked = new List<TokenEntry>();

            MarkRevoked(key, refreshToken);

            if (!string.IsNullOrEmpty(key) && _entries.TryGetValue(key, out var byKey))
            {
                byKey.IsRevoked = true;
                if (_entries.TryRemove(key, out _))
                    revoked.Add(byKey);
            }

            if (!string.IsNullOrEmpty(accessToken) || !string.IsNullOrEmpty(refreshToken))
            {
                foreach (var entry in _entries.Values.ToArray())
                {
                    var tokens = entry.Tokens;
                    var matchAccess = !string.IsNullOrEmpty(accessToken) &&
                                      string.Equals(tokens.AccessToken, accessToken, StringComparison.Ordinal);
                    var matchRefresh = !string.IsNullOrEmpty(refreshToken) &&
                                       string.Equals(tokens.RefreshToken, refreshToken, StringComparison.Ordinal);
                    if (!matchAccess && !matchRefresh)
                        continue;

                    entry.IsRevoked = true;
                    if (_entries.TryRemove(entry.Key, out _))
                        revoked.Add(entry);
                }
            }

            foreach (var entry in revoked)
                MarkRevoked(entry.Key, entry.Tokens.RefreshToken);

            return revoked;
        }

        public void MarkRevoked(string? key, string? refreshToken)
        {
            var now = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(key))
                _revoked[KeyMarker(key)] = now;
            if (!string.IsNullOrEmpty(refreshToken))
                _revoked[RefreshMarker(refreshToken)] = now;
        }

        public bool IsRevoked(string? key, string? refreshToken)
        {
            if (!string.IsNullOrEmpty(key) && IsMarked(KeyMarker(key)))
                return true;
            return !string.IsNullOrEmpty(refreshToken) && IsMarked(RefreshMarker(refreshToken));
        }

        public void PurgeExpiredTombstones()
        {
            var limit = DateTime.UtcNow - RevokedTtl;
            foreach (var pair in _revoked)
            {
                if (pair.Value < limit)
                    _revoked.TryRemove(pair.Key, out _);
            }
        }

        public TokenEntry[] Snapshot() => _entries.Values.ToArray();

        private bool IsMarked(string marker)
            => _revoked.TryGetValue(marker, out var at) && DateTime.UtcNow - at <= RevokedTtl;

        private static string KeyMarker(string key) => "k:" + key;

        private static string RefreshMarker(string refreshToken)
            => "r:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
    }
}