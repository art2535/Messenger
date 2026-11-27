namespace Messenger.Application.DTOs.Broadcasts
{
    public class CreateBroadcastRequest
    {
        public string Title { get; set; } = null!;
        public string MessageText { get; set; } = null!;
        public List<Guid> RecipientIds { get; set; } = new List<Guid>();
    }
}
