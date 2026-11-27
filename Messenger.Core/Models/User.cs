
namespace Messenger.Core.Models;

public partial class User
{
    public Guid UserId { get; set; }
    public string LastName { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public DateOnly RegistrationDate { get; set; }
    public string? Login { get; set; }
    public string? ExternalId { get; set; }
    public virtual AccountSetting Account { get; set; } = null!;
    public virtual ICollection<ChatParticipant> ChatParticipants { get; set; } = new List<ChatParticipant>();
    public virtual ICollection<Chat> Chats { get; set; } = new List<Chat>();
    public virtual ICollection<Message> MessageSenders { get; set; } = new List<Message>();
    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public virtual ICollection<Reaction> Reactions { get; set; } = new List<Reaction>();
    public virtual UserStatus? UserStatus { get; set; }
    public virtual ICollection<Role> Roles { get; set; } = new List<Role>();
    public virtual ICollection<Broadcast> BroadcastsCreated { get; set; } = new List<Broadcast>();
    public virtual ICollection<BroadcastRecipient> BroadcastRecipients { get; set; } = new List<BroadcastRecipient>();
}
