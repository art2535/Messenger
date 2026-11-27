using Messenger.Application.DTOs.Push;

namespace Messenger.Application.Interfaces;

public interface IPushSubscriptionService
{
    Task<PushSettingsDto?> GetPushSettingsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task SavePushSettingsAsync(
        Guid userId,
        UpdatePushSettingsDto settings,
        CancellationToken cancellationToken = default);

    Task AddSubscriptionAsync(
        Guid userId,
        CreatePushSubscriptionDto subscription,
        CancellationToken cancellationToken = default);

    Task RemoveSubscriptionAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task RemoveByEndpointAsync(
        string endpoint,
        CancellationToken cancellationToken = default);

    Task EnsurePushEnabledAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task UpdateSubscriptionAsync(
        UpdatePushSubscriptionDto subscription,
        CancellationToken cancellationToken = default);

    Task SendPushToOfflineUsersAsync(
        Guid chatId,
        Guid senderId,
        string senderName,
        string? messageText,
        bool hasAttachments,
        bool isMention = false,
        CancellationToken cancellationToken = default);
}
