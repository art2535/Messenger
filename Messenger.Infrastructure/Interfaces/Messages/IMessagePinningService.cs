using Messenger.Core.Models;

namespace Messenger.Infrastructure.Services
{
    public interface IMessagePinningService
    {
        Task<Message?> PinMessageAsync(
            Guid messageId,
            Guid userId,
            CancellationToken token = default);

        Task<Message?> UnpinMessageAsync(
            Guid messageId,
            Guid userId,
            CancellationToken token = default);

        Task<List<Message>> GetPinnedMessagesAsync(
            Guid chatId,
            CancellationToken token = default);
    }
}
