namespace Messenger.Infrastructure.Interfaces.Messages
{
    public interface IMessageReadService
    {
        Task<int> MarkMessagesAsReadAsync(
            Guid chatId,
            Guid readerId,
            CancellationToken token = default);
    }
}
