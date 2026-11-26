namespace Messenger.Core.DTOs.AccountSettings
{
    public class AccountSettingsDto
    {
        public bool PushEnabled { get; set; } = true;
        public bool NotifyMessages { get; set; } = true;
        public bool NotifyGroupChats { get; set; } = true;
        public bool NotifyMentions { get; set; } = true;
        public bool NotifyReactions { get; set; } = true;
        public bool NotifySystem { get; set; } = true;
        public bool NotifySound { get; set; } = true;
        public bool SuppressOpenChat { get; set; } = true;
        public bool NotifyPreview { get; set; } = true;
        public string Theme { get; set; } = "system";
        public bool LargeFont { get; set; }
        public bool CompactList { get; set; }
        public bool ShowOnline { get; set; } = true;
        public bool ShowTyping { get; set; } = true;
        public bool ReadReceipts { get; set; } = true;
        public bool IdleLogout { get; set; } = true;
    }

    public class UpdateAccountSettingsRequest : AccountSettingsDto
    {
    }

    public class LoginSessionDto
    {
        public Guid LoginId { get; set; }
        public string IpAddress { get; set; } = "";
        public DateTime LoginTime { get; set; }
        public DateTime? LogoutTime { get; set; }
        public bool Active { get; set; }
        public bool IsCurrent { get; set; }
    }
}
