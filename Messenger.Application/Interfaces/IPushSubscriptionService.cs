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
        Guid userId,
        Guid id,
        CancellationToken cancellationToken = default);

    Task RemoveByEndpointAsync(
        Guid userId,
        string endpoint,
        CancellationToken cancellationToken = default);

    Task EnsurePushEnabledAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task UpdateSubscriptionAsync(
        Guid userId,
        UpdatePushSubscriptionDto subscription,
        CancellationToken cancellationToken = default);

    Task SendPushForMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default);
}
