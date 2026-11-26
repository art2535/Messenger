namespace Messenger.Core.Interfaces;

public interface IMessageNotificationOutboxRecoveryService
{
    Task<int> RecoverDeadLettersAsync(
        int take = 100,
        CancellationToken cancellationToken = default);
}
