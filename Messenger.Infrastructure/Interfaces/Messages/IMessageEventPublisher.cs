using Messenger.Core.Messages;

namespace Messenger.Infrastructure.Interfaces.Messages
{
    public interface IMessageEventPublisher
    {
        Task PublishChatMessageAsync(
            ChatMessageSent message,
            CancellationToken cancellationToken = default);
    }
}
