
namespace Messenger.Core.Models;

public partial class ChatParticipant
{
    public Guid ChatId { get; set; }
    public Guid UserId { get; set; }
    public string? Role { get; set; }
    public DateTime JoinDate { get; set; }
    public virtual Chat Chat { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}
