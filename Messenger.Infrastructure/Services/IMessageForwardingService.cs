namespace Messenger.Infrastructure.Services
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
