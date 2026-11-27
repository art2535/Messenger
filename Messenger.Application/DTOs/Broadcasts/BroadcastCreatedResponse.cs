namespace Messenger.Application.DTOs.Broadcasts
{
    public class BroadcastCreatedResponse
    {
        public Guid BroadcastId { get; set; }
        public int TotalRecipients { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
