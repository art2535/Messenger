using System.Text.Json.Serialization;

namespace Messenger.Core.Models;

/// <summary>
/// Пользовательские настройки аккаунта: уведомления, внешний вид и конфиденциальность.
/// </summary>
public partial class AccountSetting
{
    public Guid AccountId { get; set; }
    public string? Avatar { get; set; }
    public bool PushEnabled { get; set; } = true;
    public bool NotifyMessages { get; set; } = true;
    public bool NotifyGroupChats { get; set; } = true;
    public bool NotifyMentions { get; set; } = true;
    public bool NotifyReactions { get; set; } = true;
    public bool NotifySystem { get; set; } = true;
    public bool NotifySound { get; set; } = true;
    public bool SuppressOpenChat { get; set; } = true;
    public bool NotifyPreview { get; set; } = true;
    /// <summary>light | dark | system</summary>
    public string Theme { get; set; } = "system";
    public bool LargeFont { get; set; }
    public bool CompactList { get; set; }
    public bool ShowOnline { get; set; } = true;
    public bool ShowTyping { get; set; } = true;
    public bool ReadReceipts { get; set; } = true;
    public bool IdleLogout { get; set; } = true;
    [JsonIgnore]
    public virtual User User { get; set; } = null!;
}
