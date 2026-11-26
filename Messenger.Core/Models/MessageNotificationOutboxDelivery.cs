using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Messenger.Core.Models;

public sealed class MessageNotificationOutboxDelivery
{
    [Key]
    [Column("Delivery_ID")]
    public Guid DeliveryId { get; set; }

    [Column("Outbox_ID")]
    public Guid OutboxId { get; set; }

    [Column("Message_ID")]
    public Guid MessageId { get; set; }

    [Column("Recipient_ID")]
    public Guid RecipientId { get; set; }

    [Column("Subscription_ID")]
    public Guid SubscriptionId { get; set; }

    [Column("Channel")]
    [MaxLength(32)]
    public string Channel { get; set; } = "Push";

    [Column("Status")]
    [MaxLength(32)]
    public string Status { get; set; } = "Pending";

    [Column("Attempt_Count")]
    public int AttemptCount { get; set; }

    [Column("Next_Attempt_At", TypeName = "timestamp with time zone")]
    public DateTime NextAttemptAt { get; set; }

    [Column("Locked_Until", TypeName = "timestamp with time zone")]
    public DateTime? LockedUntil { get; set; }

    [Column("Notification_ID")]
    public Guid? NotificationId { get; set; }

    [Column("Idempotency_Key")]
    [MaxLength(128)]
    public string IdempotencyKey { get; set; } = null!;

    [Column("Delivered_At", TypeName = "timestamp with time zone")]
    public DateTime? DeliveredAt { get; set; }

    [Column("Last_Error")]
    public string? LastError { get; set; }
}
