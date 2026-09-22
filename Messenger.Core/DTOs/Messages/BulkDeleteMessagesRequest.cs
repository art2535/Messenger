namespace Messenger.Core.DTOs.Messages
{
    public class BulkDeleteMessagesRequest
    {
        public Guid ChatId { get; set; }
        public List<Guid> MessageIds { get; set; } = new();
    }
}
