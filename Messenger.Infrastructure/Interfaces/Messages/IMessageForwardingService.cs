namespace Messenger.Infrastructure.Interfaces.Messages
{
    public interface IMessageForwardingService
    {
        Task<MessageForwardingResult> ForwardAsync(
            Guid senderId,
            IReadOnlyCollection<Guid> messageIds,
            IReadOnlyCollection<Guid> targetChatIds,
            CancellationToken cancellationToken = default);
    }
}
