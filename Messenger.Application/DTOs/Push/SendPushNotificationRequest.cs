namespace Messenger.Application.DTOs.Push
{
    public class SendPushNotificationRequest
    {
        public Guid ChatId { get; set; }
        public string? SenderName { get; set; }
        public string? MessageText { get; set; }
        public bool HasAttachments { get; set; }
        public bool IsMention { get; set; } = false;
    }
}
