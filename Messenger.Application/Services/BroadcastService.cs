using Messenger.Application.DTOs.Broadcasts;
using Messenger.Application.Interfaces;

namespace Messenger.Application.Services;

/// <summary>
/// Сценарии работы с массовыми рассылками.
/// </summary>
public sealed class BroadcastService : IBroadcastService
{
    private readonly IBroadcastRepository _repository;

    public BroadcastService(IBroadcastRepository repository)
    {
        _repository = repository;
    }

    public async Task<BroadcastCreatedResponse> CreateBroadcastAsync(
        CreateBroadcastRequest request,
        Guid senderId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var recipientIds = await _repository.GetExistingRecipientIdsAsync(
            request.RecipientIds,
            cancellationToken);

        if (recipientIds.Count != request.RecipientIds.Count)
            throw new ArgumentException("Один или несколько получателей не существуют");

        return await _repository.CreateBroadcastAsync(
            request,
            senderId,
            recipientIds,
            cancellationToken);
    }

    public async Task<BroadcastSummaryDto?> GetBroadcastSummaryAsync(
        Guid id,
        Guid currentUserId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var summary = await _repository.GetBroadcastSummaryAsync(id, cancellationToken);
        if (summary is null)
            return null;

        if (summary.SenderId != currentUserId && !isAdmin)
            throw new UnauthorizedAccessException("Нет прав на просмотр этой рассылки");

        return summary;
    }

    public async Task<MarkAsReadResponse> MarkAsReadAsync(
        Guid broadcastId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var result = await _repository.MarkAsReadAsync(broadcastId, userId, cancellationToken);
        return result ?? throw new KeyNotFoundException("Вы не являетесь получателем этой рассылки");
    }

    public Task<List<BroadcastInboxItemDto>> GetMyBroadcastsAsync(
        Guid userId,
        bool unreadOnly = true,
        CancellationToken cancellationToken = default)
        => _repository.GetMyBroadcastsAsync(userId, unreadOnly, cancellationToken);
}
