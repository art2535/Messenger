namespace Messenger.Infrastructure.Caching.Models
{
    public sealed class CachedMessagesPage
    {
        public List<CachedMessageItem> Items { get; set; } = new();
        public bool HasMore { get; set; }
    }
}