namespace Messenger.Application.DTOs.Broadcasts
{
    public class BroadcastRecipientDto
    {
        public Guid UserId { get; set; }
        public DateTime SentAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public bool IsRead { get; set; }
        public DateTime? ReadAt { get; set; }
    }
}
