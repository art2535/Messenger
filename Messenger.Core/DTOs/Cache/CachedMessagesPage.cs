namespace Messenger.Core.DTOs.Cache
{
    public sealed class CachedMessagesPage
    {
        public List<CachedMessageItem> Items { get; set; } = new();
        public bool HasMore { get; set; }
    }
}
