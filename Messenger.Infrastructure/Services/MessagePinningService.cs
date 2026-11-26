using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Services
{
    public class MessagePinningService : IMessagePinningService
    {
        private readonly GuapMessengerContext _context;
        private readonly IMessagePaginationService _messagePaginationService;

        public MessagePinningService(
            GuapMessengerContext context,
            IMessagePaginationService messagePaginationService)
        {
            _context = context;
            _messagePaginationService = messagePaginationService;
        }

        public async Task<Message?> PinMessageAsync(
            Guid messageId,
            Guid userId,
            CancellationToken token = default)
        {
            var message = await _context.Messages
                .FirstOrDefaultAsync(m => m.MessageId == messageId, token);
            if (message == null)
                return null;

            var isParticipant = await _context.ChatParticipants
                .AsNoTracking()
                .AnyAsync(p => p.ChatId == message.ChatId && p.UserId == userId, token);
            if (!isParticipant)
                throw new UnauthorizedAccessException("Вы не участник этого чата");

            if (message.IsPinned)
                return message;

            message.IsPinned = true;
            message.PinnedAt = UtcDateTime.Now;
            await _context.SaveChangesAsync(token);
            await _messagePaginationService.InvalidateMessageCachesAsync(message.ChatId, token);

            return message;
        }

        public async Task<Message?> UnpinMessageAsync(
            Guid messageId,
            Guid userId,
            CancellationToken token = default)
        {
            var message = await _context.Messages
                .FirstOrDefaultAsync(m => m.MessageId == messageId, token);
            if (message == null)
                return null;

            var isParticipant = await _context.ChatParticipants
                .AsNoTracking()
                .AnyAsync(p => p.ChatId == message.ChatId && p.UserId == userId, token);
            if (!isParticipant)
                throw new UnauthorizedAccessException("Вы не участник этого чата");

            if (!message.IsPinned)
                return message;

            message.IsPinned = false;
            message.PinnedAt = null;
            await _context.SaveChangesAsync(token);
            await _messagePaginationService.InvalidateMessageCachesAsync(message.ChatId, token);

            return message;
        }

        public async Task<List<Message>> GetPinnedMessagesAsync(
            Guid chatId,
            CancellationToken token = default)
        {
            return await _context.Messages
                .AsNoTracking()
                .Where(m => m.ChatId == chatId && m.IsPinned)
                .OrderBy(m => m.PinnedAt)
                .ThenBy(m => m.SequenceNumber)
                .Include(m => m.Sender)
                .ToListAsync(token);
        }
    }
}
