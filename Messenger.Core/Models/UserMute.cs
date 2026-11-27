namespace Messenger.Core.Models;

/// <summary>
/// Ограничение пользователя на отправку сообщений.
/// </summary>
public class UserMute
{
    public Guid MuteId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ChatId { get; set; }
    public Guid MutedBy { get; set; }
    public string? Reason { get; set; }
    public DateTime MutedUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}
