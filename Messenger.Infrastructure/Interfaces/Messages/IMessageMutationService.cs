using Messenger.Core.Models;

namespace Messenger.Infrastructure.Interfaces.Messages
{
    public interface IMessageMutationService
    {
        Task DeleteMessageAsync(Guid messageId, CancellationToken token = default);
        Task UpdateMessageAsync(Message message, CancellationToken token = default);
        Task<int> DeleteMessagesAsync(
            IEnumerable<Guid> messageIds,
            CancellationToken token = default);
    }
}
