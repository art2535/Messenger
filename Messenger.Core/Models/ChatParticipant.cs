using System.Text.Json.Serialization;

namespace Messenger.Core.Models;

public partial class ChatParticipant
{
    public Guid ChatId { get; set; }
    public Guid UserId { get; set; }
    public string? Role { get; set; }
    public DateTime JoinDate { get; set; }

    [JsonIgnore]
    public virtual Chat Chat { get; set; } = null!;

    [JsonIgnore]
    public virtual User User { get; set; } = null!;
}
