using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Repositories;

namespace Messenger.Infrastructure.Services
{
    public class ReactionService : IReactionService
    {
        private readonly ReactionRepository _reactionRepository;

        public ReactionService(ReactionRepository reactionRepository)
        {
            _reactionRepository = reactionRepository;
        }

        public async Task AddReactionAsync(Reaction reaction, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(reaction.ReactionType))
                throw new ArgumentException("Тип реакции обязателен", nameof(reaction.ReactionType));

            if (reaction.ReactionType.Length > 30)
                throw new ArgumentException("Тип реакции не должен превышать 30 символов", nameof(reaction.ReactionType));

            if (!await _reactionRepository.MessageExistsAsync(reaction.MessageId, cancellationToken))
                throw new KeyNotFoundException("Сообщение не найдено");

            await _reactionRepository.AddReactionAsync(reaction, cancellationToken);
        }

        public async Task<IEnumerable<Reaction>> GetReactionsByMessageIdAsync(Guid messageId,
            CancellationToken cancellationToken = default)
        {
            return await _reactionRepository.GetByMessageIdAsync(messageId, cancellationToken);
        }

        public async Task DeleteReactionAsync(Guid messageId, Guid userId, CancellationToken cancellationToken = default)
        {
            await _reactionRepository.DeleteReactionAsync(messageId, userId, cancellationToken);
        }

        public async Task<bool> MessageExistsAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            return await _reactionRepository.MessageExistsAsync(messageId, cancellationToken);
        }

        public async Task<Guid?> GetChatIdByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            return await _reactionRepository.GetChatIdByMessageIdAsync(messageId, cancellationToken);
        }
    }
}
