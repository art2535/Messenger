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
        Guid senderId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var recipientIds = await _repository.GetExistingRecipientIdsAsync(request.RecipientIds);
        if (recipientIds.Count != request.RecipientIds.Count)
            throw new ArgumentException("Один или несколько получателей не существуют");

        return await _repository.CreateBroadcastAsync(request, senderId, recipientIds);
    }

    public async Task<BroadcastSummaryDto?> GetBroadcastSummaryAsync(
        Guid id,
        Guid currentUserId,
        bool isAdmin)
    {
        var summary = await _repository.GetBroadcastSummaryAsync(id);
        if (summary is null)
            return null;

        if (summary.SenderId != currentUserId && !isAdmin)
            throw new UnauthorizedAccessException("Нет прав на просмотр этой рассылки");

        return summary;
    }

    public async Task<MarkAsReadResponse> MarkAsReadAsync(Guid broadcastId, Guid userId)
    {
        var result = await _repository.MarkAsReadAsync(broadcastId, userId);
        return result ?? throw new KeyNotFoundException("Вы не являетесь получателем этой рассылки");
    }

    public Task<List<BroadcastInboxItemDto>> GetMyBroadcastsAsync(Guid userId, bool unreadOnly = true)
        => _repository.GetMyBroadcastsAsync(userId, unreadOnly);
}
