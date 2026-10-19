using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Messenger.Core.Models;

public partial class Chat
{
    [Key]
    [Column("Chat_ID")]
    public Guid ChatId { get; set; }

    [StringLength(100)]
    public string Name { get; set; } = null!;

    [StringLength(20)]
    public string Type { get; set; } = null!;

    [Column("User_ID")]
    public Guid UserId { get; set; }

    [Column("Creation_Date", TypeName = "timestamp with time zone")]
    public DateTime CreationDate { get; set; }

    [InverseProperty("Chat")]
    [JsonIgnore]
    public virtual ICollection<ChatParticipant> ChatParticipants { get; set; } = new List<ChatParticipant>();

    [InverseProperty("Chat")]
    [JsonIgnore]
    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();

    [ForeignKey("UserId")]
    [InverseProperty("Chats")]
    [JsonIgnore]
    public virtual User User { get; set; } = null!;
}
