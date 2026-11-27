
namespace Messenger.Core.Models;

public partial class Chat
{
    public Guid ChatId { get; set; }
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;
    public Guid UserId { get; set; }
    public DateTime CreationDate { get; set; }
    public virtual ICollection<ChatParticipant> ChatParticipants { get; set; } = new List<ChatParticipant>();
    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
    public virtual User User { get; set; } = null!;
}
