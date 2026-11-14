using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Messenger.Core.Models;

[Table("Message_Reports")]
public class MessageReport
{
    [Key]
    [Column("Report_ID")]
    public Guid ReportId { get; set; }

    [Column("Message_ID")]
    public Guid MessageId { get; set; }

    [Column("Chat_ID")]
    public Guid ChatId { get; set; }

    [Column("Reporter_ID")]
    public Guid ReporterId { get; set; }

    [Column("Reason")]
    [StringLength(64)]
    public string Reason { get; set; } = null!;

    [Column("Comment")]
    [StringLength(1000)]
    public string? Comment { get; set; }

    /// <summary>pending | reviewed | dismissed | action_taken</summary>
    [Column("Status")]
    [StringLength(32)]
    public string Status { get; set; } = "pending";

    [Column("Created_At", TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }

    [Column("Reviewed_At", TypeName = "timestamp with time zone")]
    public DateTime? ReviewedAt { get; set; }

    [Column("Reviewed_By")]
    public Guid? ReviewedBy { get; set; }

    [Column("Admin_Note")]
    [StringLength(1000)]
    public string? AdminNote { get; set; }

    [ForeignKey("MessageId")]
    [JsonIgnore]
    public virtual Message? Message { get; set; }
}