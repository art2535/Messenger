using Messenger.Core.Interfaces;

namespace Messenger.Infrastructure.Services
{
    public sealed class MessageDeletionWorkflowService : IMessageDeletionWorkflowService
    {
        private readonly IMessageService _messageService;
        private readonly IChatService _chatService;
        private readonly IMessageRealtimeNotifier _realtimeNotifier;

        public MessageDeletionWorkflowService(
            IMessageService messageService,
            IChatService chatService,
            IMessageRealtimeNotifier realtimeNotifier)
        {
            _messageService = messageService;
            _chatService = chatService;
            _realtimeNotifier = realtimeNotifier;
        }

        public async Task<MessageDeletionStatus> DeleteAsync(
            Guid chatId,
            Guid messageId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var message = await _messageService.GetMessageByIdAsync(
                chatId,
                messageId,
                cancellationToken);

            if (message == null)
            {
                return MessageDeletionStatus.NotFound;
            }

            var chat = await _chatService.GetChatByIdAsync(chatId, cancellationToken);
            if (chat == null || !chat.ChatParticipants.Any(
                participant => participant.UserId == userId))
            {
                return MessageDeletionStatus.Forbidden;
            }

            await _messageService.DeleteMessageAsync(messageId, cancellationToken);

            var deletedPayload = new
            {
                MessageId = messageId,
                ChatId = chatId
            };

            await _realtimeNotifier.SendToChatAndUsersAsync(
                chatId,
                chat.ChatParticipants.Select(participant => participant.UserId),
                "MessageDeleted",
                deletedPayload,
                cancellationToken);

            return MessageDeletionStatus.Deleted;
        }
    }
}
