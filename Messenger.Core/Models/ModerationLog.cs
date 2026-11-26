using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Messenger.Core.Models;

[Table("Moderation_Logs")]
public class ModerationLog
{
    [Key]
    [Column("Log_ID")]
    public Guid LogId { get; set; }

    [Column("Actor_ID")]
    public Guid? ActorId { get; set; }

    [Column("Target_User_ID")]
    public Guid? TargetUserId { get; set; }

    [Column("Chat_ID")]
    public Guid? ChatId { get; set; }

    [Column("Message_ID")]
    public Guid? MessageId { get; set; }

    /// <summary>report | mute | unmute | auto_block_link | auto_block_file | delete_message | dismiss_report</summary>
    [Column("Action")]
    [StringLength(64)]
    public string Action { get; set; } = null!;

    [Column("Details")]
    [StringLength(2000)]
    public string? Details { get; set; }

    [Column("Created_At", TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }
}
