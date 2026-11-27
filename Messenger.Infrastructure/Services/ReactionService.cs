using Messenger.Application.Interfaces;
using Messenger.Application.DTOs.Reactions;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Messenger.Infrastructure.Services
{
    public class ReactionService : IReactionService
    {
        private readonly GuapMessengerContext _context;

        public ReactionService(GuapMessengerContext context)
        {
            _context = context;
        }

        public async Task<ReactionDto> AddReactionAsync(Guid messageId, Guid userId, string reactionType, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(reactionType))
                throw new ArgumentException("Тип реакции обязателен", nameof(reactionType));

            if (reactionType.Length > 30)
                throw new ArgumentException("Тип реакции не должен превышать 30 символов", nameof(reactionType));

            if (!await MessageExistsAsync(messageId, cancellationToken))
                throw new KeyNotFoundException("Сообщение не найдено");

            var existing = await _context.Reactions
                .FirstOrDefaultAsync(
                    r => r.MessageId == messageId && r.UserId == userId,
                    cancellationToken);

            Reaction reaction;
            if (existing != null)
            {
                existing.ReactionType = reactionType;
                reaction = existing;
                await _context.SaveChangesAsync(cancellationToken);
            }
            else
            {
                reaction = new Reaction
                {
                    ReactionId = Guid.NewGuid(),
                    MessageId = messageId,
                    UserId = userId,
                    ReactionType = reactionType
                };

                await _context.Reactions.AddAsync(reaction, cancellationToken);

                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (
                    ex.InnerException is PostgresException
                    {
                        SqlState: PostgresErrorCodes.UniqueViolation
                    })
                {
                    // Другой запрос мог добавить реакцию после нашей проверки.
                    // Уникальный индекс сохраняет инвариант, а повторный запрос
                    // превращает конкурентную вставку в обновление существующей реакции.
                    _context.Entry(reaction).State = EntityState.Detached;

                    var concurrentReaction = await _context.Reactions.FirstOrDefaultAsync(
                        r => r.MessageId == messageId && r.UserId == userId,
                        cancellationToken);
                    if (concurrentReaction is null)
                        throw;

                    reaction = concurrentReaction;
                    reaction.ReactionType = reactionType;
                    await _context.SaveChangesAsync(cancellationToken);
                }
            }

            var userName = await _context.Users
                .AsNoTracking()
                .Where(u => u.UserId == userId)
                .Select(u => ($"{u.FirstName} {u.LastName}").Trim())
                .FirstOrDefaultAsync(cancellationToken);

            return new ReactionDto
            {
                ReactionId = reaction.ReactionId,
                MessageId = reaction.MessageId,
                UserId = reaction.UserId,
                ReactionType = reaction.ReactionType,
                UserName = userName
            };
        }

        public async Task<IEnumerable<ReactionDto>> GetReactionsByMessageIdAsync(Guid messageId,
            CancellationToken cancellationToken = default)
        {
            return await _context.Reactions
                .AsNoTracking()
                .Where(r => r.MessageId == messageId)
                .OrderBy(r => r.ReactionType)
                .Select(r => new ReactionDto
                {
                    ReactionId = r.ReactionId,
                    MessageId = r.MessageId,
                    UserId = r.UserId,
                    ReactionType = r.ReactionType,
                    UserName = ($"{r.User.FirstName} {r.User.LastName}").Trim()
                })
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
