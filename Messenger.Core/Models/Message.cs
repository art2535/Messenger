using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Messenger.Core.Models;

public partial class Message
{
    [Key]
    [Column("Message_ID")]
    public Guid MessageId { get; set; }

    [Column("Sender_ID")]
    public Guid SenderId { get; set; }

    [Column("Chat_ID")]
    public Guid ChatId { get; set; }

    [Column("Message_Text")]
    public string MessageText { get; set; } = null!;

    [Column("Has_Attachments")]
    public bool HasAttachments { get; set; }

    [Column("Send_Time", TypeName = "timestamp with time zone")]
    public DateTime SendTime { get; set; }

    [Column("Sequence_Number")]
    public long SequenceNumber { get; set; }

    [Column("Delivery_Status")]
    public MessageDeliveryStatus DeliveryStatus { get; set; }

    [Column("Read_Time", TypeName = "timestamp with time zone")]
    public DateTime? ReadTime { get; set; }

    [InverseProperty("Message")]
    [JsonIgnore]
    public virtual ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();

    [ForeignKey("ChatId")]
    [InverseProperty("Messages")]
    [JsonIgnore]
    public virtual Chat Chat { get; set; } = null!;

    [InverseProperty("Message")]
    [JsonIgnore]
    public virtual ICollection<Reaction> Reactions { get; set; } = new List<Reaction>();

    [ForeignKey("SenderId")]
    [InverseProperty("MessageSenders")]
    [JsonIgnore]
    public virtual User Sender { get; set; } = null!;
}
