using System.Text.Json.Serialization;

namespace Messenger.Core.Models;

public partial class User
{
    public Guid UserId { get; set; }
    public string LastName { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public DateOnly RegistrationDate { get; set; }
    public string? Login { get; set; }
    public string? ExternalId { get; set; }

    [JsonIgnore]
    public virtual AccountSetting Account { get; set; } = null!;
    [JsonIgnore]
    public virtual ICollection<ChatParticipant> ChatParticipants { get; set; } = new List<ChatParticipant>();
    [JsonIgnore]
    public virtual ICollection<Chat> Chats { get; set; } = new List<Chat>();
    [JsonIgnore]
    public virtual ICollection<Message> MessageSenders { get; set; } = new List<Message>();
    [JsonIgnore]
    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    [JsonIgnore]
    public virtual ICollection<Reaction> Reactions { get; set; } = new List<Reaction>();
    [JsonIgnore]
    public virtual UserStatus? UserStatus { get; set; }
    [JsonIgnore]
    public virtual ICollection<Role> Roles { get; set; } = new List<Role>();
    [JsonIgnore]
    public virtual ICollection<Broadcast> BroadcastsCreated { get; set; } = new List<Broadcast>();
    [JsonIgnore]
    public virtual ICollection<BroadcastRecipient> BroadcastRecipients { get; set; } = new List<BroadcastRecipient>();
}
