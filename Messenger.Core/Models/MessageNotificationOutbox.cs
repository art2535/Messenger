using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Messenger.Core.Models;

public sealed class MessageNotificationOutbox
{
    [Key]
    [Column("Outbox_ID")]
    public Guid OutboxId { get; set; }

    [Column("Message_ID")]
    public Guid MessageId { get; set; }

    [Column("Chat_ID")]
    public Guid ChatId { get; set; }

    [Column("Sender_ID")]
    public Guid SenderId { get; set; }

    [Column("Sender_Name")]
    public string SenderName { get; set; } = null!;

    [Column("Payload")]
    public string Payload { get; set; } = null!;

    [Column("Message_Text")]
    public string? MessageText { get; set; }

    [Column("Has_Attachments")]
    public bool HasAttachments { get; set; }

    [Column("Status")]
    [MaxLength(32)]
    public string Status { get; set; } = "Pending";

    [Column("Attempt_Count")]
    public int AttemptCount { get; set; }

    [Column("Next_Attempt_At", TypeName = "timestamp with time zone")]
    public DateTime NextAttemptAt { get; set; }

    [Column("Locked_Until", TypeName = "timestamp with time zone")]
    public DateTime? LockedUntil { get; set; }

    [Column("Created_At", TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }

    [Column("Delivered_At", TypeName = "timestamp with time zone")]
    public DateTime? DeliveredAt { get; set; }

    [Column("Last_Error")]
    public string? LastError { get; set; }
}
