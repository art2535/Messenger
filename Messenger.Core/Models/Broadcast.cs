using System.Text.Json.Serialization;

namespace Messenger.Core.Models;

public partial class Broadcast
{
    public Guid BroadcastId { get; set; }
    public string Title { get; set; } = null!;
    public string MessageText { get; set; } = null!;
    public Guid SenderId { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalRecipients { get; set; }

    [JsonIgnore]
    public virtual User Sender { get; set; } = null!;

    [JsonIgnore]
    public virtual ICollection<BroadcastRecipient> Recipients { get; set; } = new List<BroadcastRecipient>();
}
