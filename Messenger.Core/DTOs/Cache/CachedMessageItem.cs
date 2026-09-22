using Messenger.Core.Models;

namespace Messenger.Core.DTOs.Cache
{
    public sealed class CachedMessageItem
    {
        public Guid MessageId { get; set; }
        public Guid ChatId { get; set; }
        public Guid SenderId { get; set; }
        public string? SenderFirstName { get; set; }
        public string? SenderLastName { get; set; }
        public string MessageText { get; set; } = string.Empty;
        public DateTime SendTime { get; set; }
        public long SequenceNumber { get; set; }
        public MessageDeliveryStatus DeliveryStatus { get; set; }
        public DateTime? ReadTime { get; set; }
        public bool HasAttachments { get; set; }
        public List<CachedAttachmentItem> Attachments { get; set; } = new();
        public List<CachedReactionItem> Reactions { get; set; } = new();
    }
}
