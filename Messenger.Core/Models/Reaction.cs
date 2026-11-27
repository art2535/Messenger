
namespace Messenger.Core.Models;

public partial class Reaction
{
    public Guid ReactionId { get; set; }
    public Guid MessageId { get; set; }
    public Guid UserId { get; set; }
    public string ReactionType { get; set; } = null!;
    public virtual Message Message { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}
