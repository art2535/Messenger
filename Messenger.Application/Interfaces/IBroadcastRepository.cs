using Messenger.Application.DTOs.Broadcasts;

namespace Messenger.Application.Interfaces;

/// <summary>
/// Контракт доступа к данным рассылок. Не содержит EF Core-зависимостей.
/// </summary>
public interface IBroadcastRepository
{
    Task<IReadOnlyList<Guid>> GetExistingRecipientIdsAsync(
        IReadOnlyCollection<Guid> recipientIds,
        CancellationToken cancellationToken = default);

    Task<BroadcastCreatedResponse> CreateBroadcastAsync(
        CreateBroadcastRequest request,
        Guid senderId,
        IReadOnlyCollection<Guid> recipientIds,
        CancellationToken cancellationToken = default);

    Task<BroadcastSummaryDto?> GetBroadcastSummaryAsync(
        Guid broadcastId,
        CancellationToken cancellationToken = default);

    Task<MarkAsReadResponse?> MarkAsReadAsync(
        Guid broadcastId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<List<BroadcastInboxItemDto>> GetMyBroadcastsAsync(
        Guid userId,
        bool unreadOnly = true,
        CancellationToken cancellationToken = default);
}
