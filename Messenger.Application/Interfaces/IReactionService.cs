using Messenger.Application.DTOs.Reactions;

namespace Messenger.Application.Interfaces;

public interface IReactionService
{
    Task<ReactionDto> AddReactionAsync(
        Guid messageId,
        Guid userId,
        string reactionType,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<ReactionDto>> GetReactionsByMessageIdAsync(
        Guid messageId,
        CancellationToken cancellationToken = default);

    Task DeleteReactionAsync(
        Guid messageId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> MessageExistsAsync(
        Guid messageId,
        CancellationToken cancellationToken = default);

    Task<Guid?> GetChatIdByMessageIdAsync(
        Guid messageId,
        CancellationToken cancellationToken = default);
}
