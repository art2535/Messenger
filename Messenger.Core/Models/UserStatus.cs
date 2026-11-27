using System.Text.Json.Serialization;

namespace Messenger.Core.Models;

public partial class UserStatus
{
    public Guid UserId { get; set; }
    public bool Online { get; set; }
    public DateTime? LastActivity { get; set; }

    [JsonIgnore]
    public virtual User User { get; set; } = null!;
}
