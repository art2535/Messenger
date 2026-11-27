using System.Text.Json.Serialization;

namespace Messenger.Core.Models;

public partial class Reaction
{
    public Guid ReactionId { get; set; }
    public Guid MessageId { get; set; }
    public Guid UserId { get; set; }
    public string ReactionType { get; set; } = null!;

    [JsonIgnore]
    public virtual Message Message { get; set; } = null!;

    [JsonIgnore]
    public virtual User User { get; set; } = null!;
}
