using Messenger.Application.DTOs.Broadcasts;

namespace Messenger.Application.Interfaces;

/// <summary>
/// Контракт доступа к данным рассылок. Не содержит EF Core-зависимостей.
/// </summary>
public interface IBroadcastRepository
{
    Task<IReadOnlyList<Guid>> GetExistingRecipientIdsAsync(IReadOnlyCollection<Guid> recipientIds);
    Task<BroadcastCreatedResponse> CreateBroadcastAsync(
        CreateBroadcastRequest request,
        Guid senderId,
        IReadOnlyCollection<Guid> recipientIds);
    Task<BroadcastSummaryDto?> GetBroadcastSummaryAsync(Guid broadcastId);
    Task<MarkAsReadResponse?> MarkAsReadAsync(Guid broadcastId, Guid userId);
    Task<List<BroadcastInboxItemDto>> GetMyBroadcastsAsync(Guid userId, bool unreadOnly = true);
}
