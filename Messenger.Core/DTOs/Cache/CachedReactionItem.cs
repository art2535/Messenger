namespace Messenger.Core.DTOs.Cache
{
    public sealed class CachedReactionItem
    {
        public string? ReactionType { get; set; }
        public Guid UserId { get; set; }
        public string? UserFirstName { get; set; }
        public string? UserLastName { get; set; }
    }
}
