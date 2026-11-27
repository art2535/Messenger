namespace Messenger.Application.DTOs.Broadcasts
{
    public class BroadcastSummaryDto
    {
        public Guid BroadcastId { get; set; }
        public string Title { get; set; } = null!;
        public string MessageText { get; set; } = null!;
        public Guid SenderId { get; set; }
        public DateTime CreatedAt { get; set; }
        public int TotalRecipients { get; set; }
        public int ReadCount { get; set; }
        public List<RecipientStatusDto> Recipients { get; set; } = new();
    }
}
