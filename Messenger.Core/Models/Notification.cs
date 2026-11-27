
namespace Messenger.Core.Models;

public partial class Notification
{
    public Guid NotificationId { get; set; }
    public Guid UserId { get; set; }
    public string Text { get; set; } = null!;
    public string? DeduplicationKey { get; set; }
    public DateTime CreationDate { get; set; }
    public bool Read { get; set; }
    public DateTime? ReadAt { get; set; }
    public virtual User User { get; set; } = null!;
}
