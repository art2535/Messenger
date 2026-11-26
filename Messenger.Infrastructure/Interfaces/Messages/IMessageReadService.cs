namespace Messenger.Infrastructure.Services
{
    public interface IMessageReadService
    {
        Task<int> MarkMessagesAsReadAsync(
            Guid chatId,
            Guid readerId,
            CancellationToken token = default);
    }
}
