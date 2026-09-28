using System.Collections.Concurrent;

namespace Messenger.Web.Services
{
    public sealed class UserTokenStore
    {
        public const string StoreKeyItem = ".Messenger.TokenStoreKey";

        private readonly ConcurrentDictionary<string, TokenEntry> _entries = new();

        public TokenEntry GetOrAdd(string key, string accessToken, string refreshToken)
            => _entries.GetOrAdd(key, k => new TokenEntry(
                k, TokenSet.Create(accessToken, refreshToken, TokenSet.DefaultLifetime)));

        public void Remove(string key) => _entries.TryRemove(key, out _);

        public bool RevokeAndRemove(string? key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            if (_entries.TryRemove(key, out var entry))
            {
                entry.IsRevoked = true;
                return true;
            }
            return false;
        }

        public int RevokeByToken(string? accessToken, string? refreshToken)
        {
            if (string.IsNullOrEmpty(accessToken) && string.IsNullOrEmpty(refreshToken))
                return 0;

            var removed = 0;
            foreach (var entry in _entries.Values.ToArray())
            {
                var tokens = entry.Tokens;
                var matchAccess = !string.IsNullOrEmpty(accessToken) &&
                                  string.Equals(tokens.AccessToken, accessToken, StringComparison.Ordinal);
                var matchRefresh = !string.IsNullOrEmpty(refreshToken) &&
                                   string.Equals(tokens.RefreshToken, refreshToken, StringComparison.Ordinal);
                if (matchAccess || matchRefresh)
                {
                    entry.IsRevoked = true;
                    if (_entries.TryRemove(entry.Key, out _))
                        removed++;
                }
            }
            return removed;
        }

        public TokenEntry[] Snapshot() => _entries.Values.ToArray();
    }
}