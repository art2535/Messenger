namespace Messenger.Infrastructure.Services
{
    public sealed class MessageReadFacade
    {
        private readonly IMessageReadService _messageReadService;

        public MessageReadFacade(IMessageReadService messageReadService)
        {
            _messageReadService = messageReadService;
        }

        public Task<int> MarkMessagesAsReadAsync(
            Guid chatId,
            Guid readerId,
            CancellationToken cancellationToken = default)
        {
            return _messageReadService.MarkMessagesAsReadAsync(
                chatId,
                readerId,
                cancellationToken);
        }
    }
}
