using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Messenger.Core.Models;

public partial class Login
{
    [Key]
    [Column("Login_ID")]
    public Guid LoginId { get; set; }

    [Column("User_ID")]
    public Guid UserId { get; set; }

    [Column(TypeName = "text")]
    public string Token { get; set; } = null!;

    [Column("IP_Address")]
    [StringLength(45)]
    public string IpAddress { get; set; } = null!;

    [Column("Login_Time", TypeName = "timestamp with time zone")]
    public DateTime LoginTime { get; set; }

    [Column("Logout_Time", TypeName = "timestamp with time zone")]
    public DateTime? LogoutTime { get; set; }

    public bool Active { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("Logins")]
    [JsonIgnore]
    public virtual User User { get; set; } = null!;
}
