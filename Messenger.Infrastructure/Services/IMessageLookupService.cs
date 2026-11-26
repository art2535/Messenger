using Messenger.Core.Models;

namespace Messenger.Infrastructure.Services
{
    public interface IMessageLookupService
    {
        Task<Message?> GetMessageByIdAsync(
            Guid chatId,
            Guid messageId,
            CancellationToken token = default);

        Task<Message?> GetMessageByIdGlobalAsync(
            Guid messageId,
            CancellationToken token = default);
    }
}
