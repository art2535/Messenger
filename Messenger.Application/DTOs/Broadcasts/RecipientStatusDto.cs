namespace Messenger.Application.DTOs.Broadcasts
{
    public class RecipientStatusDto
    {
        public Guid UserId { get; set; }
        public bool IsRead { get; set; }
        public DateTime? ReadAt { get; set; }
    }
}
