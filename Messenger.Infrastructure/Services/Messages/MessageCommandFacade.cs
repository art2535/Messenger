using Messenger.Core.DTOs;
using Messenger.Core.Messages;
using Messenger.Core.Models;

namespace Messenger.Infrastructure.Services.MessageCommandFacade.cs
{
    public sealed class MessageCommandFacade
    {
        private readonly IMessageCreationWorkflowService _creationWorkflowService;
        private readonly IMessageMutationService _mutationService;
        private readonly IMessagePinningService _pinningService;
        private readonly IMessageEventPublisher _eventPublisher;

        public MessageCommandFacade(
            IMessageCreationWorkflowService creationWorkflowService,
            IMessageMutationService mutationService,
            IMessagePinningService pinningService,
            IMessageEventPublisher eventPublisher)
        {
            _creationWorkflowService = creationWorkflowService;
            _mutationService = mutationService;
            _pinningService = pinningService;
            _eventPublisher = eventPublisher;
        }

        public Task<ServiceResult<Message>> SendMessageAsync(
            Guid messageId,
            Guid chatId,
            Guid senderId,
            string? content,
            bool hasAttachments,
            CancellationToken token = default)
        {
            return _creationWorkflowService.SendMessageAsync(
                messageId,
                chatId,
                senderId,
                content,
                hasAttachments,
                token);
        }

        public Task PublishChatMessageAsync(
            ChatMessageSent message,
            CancellationToken cancellationToken = default)
        {
            return _eventPublisher.PublishChatMessageAsync(message, cancellationToken);
        }

        public Task DeleteMessageAsync(Guid messageId, CancellationToken token = default)
        {
            return _mutationService.DeleteMessageAsync(messageId, token);
        }

        public Task UpdateMessageAsync(Message message, CancellationToken token = default)
        {
            return _mutationService.UpdateMessageAsync(message, token);
        }

        public Task<int> DeleteMessagesAsync(
            IEnumerable<Guid> messageIds,
            CancellationToken token = default)
        {
            return _mutationService.DeleteMessagesAsync(messageIds, token);
        }

        public Task<Message?> PinMessageAsync(
            Guid messageId,
            Guid userId,
            CancellationToken token = default)
        {
            return _pinningService.PinMessageAsync(messageId, userId, token);
        }

        public Task<Message?> UnpinMessageAsync(
            Guid messageId,
            Guid userId,
            CancellationToken token = default)
        {
            return _pinningService.UnpinMessageAsync(messageId, userId, token);
        }
    }
}
