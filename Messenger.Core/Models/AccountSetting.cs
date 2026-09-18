using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Core.Models;

[Table("Account_Settings")]
[Index("AccountId", Name = "Account_Settings_Account_ID_key", IsUnique = true)]
public partial class AccountSetting
{
    [Key]
    [Column("Setting_ID")]
    public Guid SettingId { get; set; }

    [Column("Account_ID")]
    public Guid AccountId { get; set; }

    public string? Avatar { get; set; }

    [Column("Push_Enabled")]
    public bool PushEnabled { get; set; } = true;

    [Column("Notify_Messages")]
    public bool NotifyMessages { get; set; } = true;

    [Column("Notify_GroupChats")]
    public bool NotifyGroupChats { get; set; } = true;

    [Column("Notify_Mentions")]
    public bool NotifyMentions { get; set; } = true;

    [InverseProperty("Account")]
    [JsonIgnore]
    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
