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

    [Column("Notify_Reactions")]
    public bool NotifyReactions { get; set; } = true;

    [Column("Notify_System")]
    public bool NotifySystem { get; set; } = true;

    [Column("Notify_Sound")]
    public bool NotifySound { get; set; } = true;

    [Column("Suppress_Open_Chat")]
    public bool SuppressOpenChat { get; set; } = true;

    [Column("Notify_Preview")]
    public bool NotifyPreview { get; set; } = true;

    /// <summary>light | dark | system</summary>
    [Column("Theme")]
    [StringLength(16)]
    public string Theme { get; set; } = "system";

    [Column("Large_Font")]
    public bool LargeFont { get; set; }

    [Column("Compact_List")]
    public bool CompactList { get; set; }

    [Column("Show_Online")]
    public bool ShowOnline { get; set; } = true;

    [Column("Show_Typing")]
    public bool ShowTyping { get; set; } = true;

    [Column("Read_Receipts")]
    public bool ReadReceipts { get; set; } = true;

    [Column("Idle_Logout")]
    public bool IdleLogout { get; set; } = true;

    [InverseProperty("Account")]
    [JsonIgnore]
    public virtual User User { get; set; } = null!;
}
