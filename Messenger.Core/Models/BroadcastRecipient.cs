
namespace Messenger.Core.Models;

public partial class BroadcastRecipient
{
    public Guid BroadcastId { get; set; }
    public Guid UserId { get; set; }
    public DateTime SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public bool IsRead { get; set; }
    public virtual Broadcast Broadcast { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}
