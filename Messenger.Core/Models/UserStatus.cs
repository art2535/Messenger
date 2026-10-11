using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Messenger.Core.Models;

[Table("User_Statuses")]
public partial class UserStatus
{
    [Key]
    [Column("User_ID")]
    public Guid UserId { get; set; }

    public bool Online { get; set; }

    [Column("Last_Activity", TypeName = "timestamp with time zone")]
    public DateTime? LastActivity { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("UserStatus")]
    [JsonIgnore]
    public virtual User User { get; set; } = null!;
}
