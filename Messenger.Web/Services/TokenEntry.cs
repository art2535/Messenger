namespace Messenger.Web.Services
{
    public sealed class TokenEntry
    {
        private volatile TokenSet _tokens;
        private volatile bool _isRevoked;

        public TokenEntry(string key, TokenSet tokens)
        {
            Key = key;
            _tokens = tokens;
            LastSeenUtc = DateTime.UtcNow;
        }

        public string Key { get; }

        public TokenSet Tokens
        {
            get => _tokens;
            set => _tokens = value;
        }

        public bool IsRevoked
        {
            get => _isRevoked;
            set => _isRevoked = value;
        }

        public DateTime LastSeenUtc { get; set; }

        public SemaphoreSlim RefreshLock { get; } = new(1, 1);
    }
}