using Messenger.Core.DTOs.Messages;

namespace Messenger.Infrastructure.Services
{
    public interface IMessageSearchService
    {
        Task<List<MessageDto>> SearchMessagesAsync(
            Guid chatId,
            string query,
            CancellationToken token = default);
    }
}