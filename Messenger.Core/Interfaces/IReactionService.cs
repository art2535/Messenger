using Messenger.Core.Models;

namespace Messenger.Core.Interfaces
{
    public interface IReactionService
    {
        Task AddReactionAsync(Reaction reaction, CancellationToken cancellationToken = default);
        Task<IEnumerable<Reaction>> GetReactionsByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default);
        Task DeleteReactionAsync(Guid messageId, Guid userId, CancellationToken cancellationToken = default);
        Task<bool> MessageExistsAsync(Guid messageId, CancellationToken cancellationToken = default);
        Task<Guid?> GetChatIdByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default);
    }
}
