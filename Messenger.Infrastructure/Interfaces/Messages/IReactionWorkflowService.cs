using Messenger.Core.Models;

namespace Messenger.Infrastructure.Interfaces.Messages
{
    public interface IReactionWorkflowService
    {
        Task<IEnumerable<Reaction>> GetReactionsByMessageIdAsync(
            Guid messageId,
            CancellationToken cancellationToken = default);

        Task AddReactionAsync(
            Guid messageId,
            User user,
            string reactionType,
            CancellationToken cancellationToken = default);

        Task DeleteReactionAsync(
            Guid messageId,
            User user,
            CancellationToken cancellationToken = default);
    }
}
