using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Messenger.Core.Models;

public class ScheduledMessage
{
    [Key]
    [Column("Scheduled_Message_ID")]
    public Guid ScheduledMessageId { get; set; }

    [Column("Chat_ID")]
    public Guid ChatId { get; set; }

    [Column("Sender_ID")]
    public Guid SenderId { get; set; }

    [Column("Message_Text")]
    public string? MessageText { get; set; }

    [Column("Has_Attachments")]
    public bool HasAttachments { get; set; }

    [Column("Attachments_Json")]
    public string? AttachmentsJson { get; set; }

    [Column("Scheduled_At", TypeName = "timestamp with time zone")]
    public DateTime ScheduledAt { get; set; }

    [Column("Created_At", TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }

    [Column("Updated_At", TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column("Status")]
    [MaxLength(32)]
    public string Status { get; set; } = ScheduledMessageStatus.Pending;

    [Column("Result_Message_ID")]
    public Guid? ResultMessageId { get; set; }

    [Column("Error_Message")]
    public string? ErrorMessage { get; set; }

    [Column("Reply_To_Message_ID")]
    public string? ReplyToMessageId { get; set; }

    [ForeignKey(nameof(ChatId))]
    [JsonIgnore]
    public virtual Chat? Chat { get; set; }

    [ForeignKey(nameof(SenderId))]
    [JsonIgnore]
    public virtual User? Sender { get; set; }
}

public static class ScheduledMessageStatus
{
    public const string Pending = "Pending";
    public const string Sent = "Sent";
    public const string Cancelled = "Cancelled";
    public const string Failed = "Failed";
}
