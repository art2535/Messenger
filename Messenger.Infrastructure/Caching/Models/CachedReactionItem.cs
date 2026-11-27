namespace Messenger.Infrastructure.Caching.Models
{
    public sealed class CachedReactionItem
    {
        public string? ReactionType { get; set; }
        public Guid UserId { get; set; }
        public string? UserFirstName { get; set; }
        public string? UserLastName { get; set; }
    }
}