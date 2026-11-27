namespace Messenger.Application.DTOs.Notifications;

public sealed class CreateNotificationDto
{
    public Guid UserId { get; init; }
    public string Text { get; init; } = string.Empty;
}

public sealed class NotificationDto
{
    public Guid NotificationId { get; init; }
    public Guid UserId { get; init; }
    public string Text { get; init; } = string.Empty;
    public DateTime CreationDate { get; init; }
    public bool Read { get; init; }
    public DateTime? ReadAt { get; init; }
}
