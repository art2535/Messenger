using System.Text.Json.Serialization;

namespace Messenger.Core.Models;

public class ScheduledMessage
{
    public Guid ScheduledMessageId { get; set; }
    public Guid ChatId { get; set; }
    public Guid SenderId { get; set; }
    public string? MessageText { get; set; }
    public bool HasAttachments { get; set; }
    public string? AttachmentsJson { get; set; }
    public DateTime ScheduledAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Status { get; set; } = ScheduledMessageStatus.Pending;
    public Guid? ResultMessageId { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ReplyToMessageId { get; set; }

    [JsonIgnore]
    public virtual Chat? Chat { get; set; }

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
