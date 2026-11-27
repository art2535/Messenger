
namespace Messenger.Core.Models;

public partial class Broadcast
{
    public Guid BroadcastId { get; set; }
    public string Title { get; set; } = null!;
    public string MessageText { get; set; } = null!;
    public Guid? SenderId { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalRecipients { get; set; }
    public virtual User? Sender { get; set; } = null;
    public virtual ICollection<BroadcastRecipient> Recipients { get; set; } = new List<BroadcastRecipient>();
}
