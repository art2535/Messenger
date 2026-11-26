using Messenger.Core.Messages;

namespace Messenger.Infrastructure.Services
{
    public interface IMessageEventPublisher
    {
        Task PublishChatMessageAsync(
            ChatMessageSent message,
            CancellationToken cancellationToken = default);
    }
}
