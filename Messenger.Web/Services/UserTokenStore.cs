using System.Collections.Concurrent;

namespace Messenger.Web.Services
{
    public sealed class UserTokenStore
    {
        private readonly ConcurrentDictionary<string, TokenEntry> _entries = new();

        public TokenEntry GetOrAdd(string key, string accessToken, string refreshToken)
            => _entries.GetOrAdd(key, k => new TokenEntry(
                k, TokenSet.Create(accessToken, refreshToken, TokenSet.DefaultLifetime)));

        public void Remove(string key) => _entries.TryRemove(key, out _);

        public TokenEntry[] Snapshot() => _entries.Values.ToArray();
    }
}