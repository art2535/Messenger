namespace Messenger.Core.Models;

/// <summary>
/// Журнал действий модерации.
/// </summary>
public class ModerationLog
{
    public Guid LogId { get; set; }
    public Guid? ActorId { get; set; }
    public Guid? TargetUserId { get; set; }
    public Guid? ChatId { get; set; }
    public Guid? MessageId { get; set; }

    /// <summary>report | mute | unmute | auto_block_link | auto_block_file | delete_message | dismiss_report</summary>
    public string Action { get; set; } = null!;

    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; }
}
