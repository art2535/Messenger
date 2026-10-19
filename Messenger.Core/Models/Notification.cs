using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Messenger.Core.Models;

public partial class Notification
{
    [Key]
    [Column("Notification_ID")]
    public Guid NotificationId { get; set; }

    [Column("User_ID")]
    public Guid UserId { get; set; }

    public string Text { get; set; } = null!;

    [Column("Creation_Date", TypeName = "timestamp with time zone")]
    public DateTime CreationDate { get; set; }

    public bool Read { get; set; }

    [Column("Read_At", TypeName = "timestamp with time zone")]
    public DateTime? ReadAt { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("Notifications")]
    [JsonIgnore]
    public virtual User User { get; set; } = null!;

}
