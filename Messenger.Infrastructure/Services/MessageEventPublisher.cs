using MassTransit;
using Messenger.Core.Messages;
using Messenger.Infrastructure.Data;

namespace Messenger.Infrastructure.Services
{
    public class MessageEventPublisher : IMessageEventPublisher
    {
        private readonly GuapMessengerContext _context;
        private readonly IPublishEndpoint _publishEndpoint;

        public MessageEventPublisher(
            GuapMessengerContext context,
            IPublishEndpoint publishEndpoint)
        {
            _context = context;
            _publishEndpoint = publishEndpoint;
        }

        public async Task PublishChatMessageAsync(
            ChatMessageSent message,
            CancellationToken cancellationToken = default)
        {
            await _publishEndpoint.Publish(message, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
