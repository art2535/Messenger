using System.Text.Json.Serialization;

namespace Messenger.Core.Models;

public partial class Notification
{
    public Guid NotificationId { get; set; }
    public Guid UserId { get; set; }
    public string Text { get; set; } = null!;
    public DateTime CreationDate { get; set; }
    public bool Read { get; set; }
    public DateTime? ReadAt { get; set; }

    [JsonIgnore]
    public virtual User User { get; set; } = null!;
}
