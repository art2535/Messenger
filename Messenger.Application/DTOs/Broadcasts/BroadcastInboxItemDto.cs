namespace Messenger.Application.DTOs.Broadcasts;

/// <summary>
/// Краткая информация о рассылке, адресованной текущему пользователю.
/// </summary>
public sealed class BroadcastInboxItemDto
{
    public Guid BroadcastId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string MessageText { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public bool IsRead { get; init; }
    public DateTime? ReadAt { get; init; }
}
