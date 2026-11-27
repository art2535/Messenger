
namespace Messenger.Core.Models;

/// <summary>
/// Жалоба пользователя на сообщение.
/// </summary>
public class MessageReport
{
    public Guid ReportId { get; set; }
    public Guid MessageId { get; set; }
    public Guid ChatId { get; set; }
    public Guid ReporterId { get; set; }
    public string Reason { get; set; } = null!;
    public string? Comment { get; set; }

    /// <summary>pending | reviewed | dismissed | action_taken</summary>
    public string Status { get; set; } = "pending";

    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public string? AdminNote { get; set; }
    public virtual Message? Message { get; set; }
}
