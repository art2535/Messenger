using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Messenger.Core.Models;

[Table("User_Mutes")]
public class UserMute
{
    [Key]
    [Column("Mute_ID")]
    public Guid MuteId { get; set; }

    [Column("User_ID")]
    public Guid UserId { get; set; }

    [Column("Chat_ID")]
    public Guid? ChatId { get; set; }

    [Column("Muted_By")]
    public Guid MutedBy { get; set; }

    [Column("Reason")]
    [StringLength(500)]
    public string? Reason { get; set; }

    [Column("Muted_Until", TypeName = "timestamp with time zone")]
    public DateTime MutedUntil { get; set; }

    [Column("Created_At", TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }

    [Column("Is_Active")]
    public bool IsActive { get; set; } = true;
}
