using System.Text.Json.Serialization;
using Messenger.Core.Enums;

namespace Messenger.Core.Models;

public partial class Message
{
    public Guid MessageId { get; set; }
    public Guid SenderId { get; set; }
    public Guid ChatId { get; set; }
    public string MessageText { get; set; } = null!;
    public bool HasAttachments { get; set; }
    public DateTime SendTime { get; set; }
    public long SequenceNumber { get; set; }
    public MessageDeliveryStatus DeliveryStatus { get; set; }
    public DateTime? ReadTime { get; set; }
    public bool IsPinned { get; set; }
    public DateTime? PinnedAt { get; set; }

    [JsonIgnore]
    public virtual ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();

    [JsonIgnore]
    public virtual Chat Chat { get; set; } = null!;

    [JsonIgnore]
    public virtual ICollection<Reaction> Reactions { get; set; } = new List<Reaction>();

    [JsonIgnore]
    public virtual User Sender { get; set; } = null!;
}
