using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Services
{
    public class ReactionService : IReactionService
    {
        private readonly GuapMessengerContext _context;

        public ReactionService(GuapMessengerContext context)
        {
            _context = context;
        }

        public async Task AddReactionAsync(Reaction reaction, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(reaction.ReactionType))
                throw new ArgumentException("Тип реакции обязателен", nameof(reaction.ReactionType));

            if (reaction.ReactionType.Length > 30)
                throw new ArgumentException("Тип реакции не должен превышать 30 символов", nameof(reaction.ReactionType));

            if (!await MessageExistsAsync(reaction.MessageId, cancellationToken))
                throw new KeyNotFoundException("Сообщение не найдено");

            var existing = await _context.Reactions
                .FirstOrDefaultAsync(
                    r => r.MessageId == reaction.MessageId && r.UserId == reaction.UserId,
                    cancellationToken);

            if (existing != null)
            {
                existing.ReactionType = reaction.ReactionType;
                reaction.ReactionId = existing.ReactionId;
            }
            else
            {
                if (reaction.ReactionId == Guid.Empty)
                    reaction.ReactionId = Guid.NewGuid();

                await _context.Reactions.AddAsync(reaction, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<IEnumerable<Reaction>> GetReactionsByMessageIdAsync(Guid messageId,
            CancellationToken cancellationToken = default)
        {
            return await _context.Reactions
                .AsNoTracking()
                .Where(r => r.MessageId == messageId)
                .Include(r => r.User)
                .OrderBy(r => r.ReactionType)
                .ToListAsync(cancellationToken);
        }

        public async Task DeleteReactionAsync(Guid messageId, Guid userId, CancellationToken cancellationToken = default)
        {
            var reaction = await _context.Reactions
                .FirstOrDefaultAsync(
                    r => r.MessageId == messageId && r.UserId == userId,
                    cancellationToken);

            if (reaction != null)
            {
                _context.Reactions.Remove(reaction);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<bool> MessageExistsAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            return await _context.Messages
                .AsNoTracking()
                .AnyAsync(m => m.MessageId == messageId, cancellationToken);
        }

        public async Task<Guid?> GetChatIdByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            return await _context.Messages
                .AsNoTracking()
                .Where(m => m.MessageId == messageId)
                .Select(m => (Guid?)m.ChatId)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}
