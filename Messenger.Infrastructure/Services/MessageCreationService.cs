using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Services
{
    public class MessageCreationService : IMessageCreationService
    {
        private readonly GuapMessengerContext _context;
        private readonly IMessagePaginationService _messagePaginationService;

        public MessageCreationService(
            GuapMessengerContext context,
            IMessagePaginationService messagePaginationService)
        {
            _context = context;
            _messagePaginationService = messagePaginationService;
        }

        public async Task CreateMessageAsync(Message message, CancellationToken token = default)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(token);
            try
            {
                var lockKey = BitConverter.ToInt64(message.ChatId.ToByteArray(), 0);
                await _context.Database.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_xact_lock({0})",
                    new object[] { lockKey },
                    token);

                var lastSeq = await _context.Messages
                    .Where(m => m.ChatId == message.ChatId)
                    .MaxAsync(m => (long?)m.SequenceNumber, token) ?? 0L;

                message.SequenceNumber = lastSeq + 1;
                message.DeliveryStatus = MessageDeliveryStatus.Pending;

                await _context.Messages.AddAsync(message, token);
                await _context.SaveChangesAsync(token);
                await transaction.CommitAsync(token);

                var forCache = await _context.Messages
                    .AsNoTracking()
                    .Include(m => m.Sender)
                    .Include(m => m.Attachments)
                    .Include(m => m.Reactions).ThenInclude(r => r.User)
                    .FirstOrDefaultAsync(m => m.MessageId == message.MessageId, token);

                if (forCache is not null)
                    await _messagePaginationService.PrependMessageToCachesAsync(forCache, token);
                else
                    await _messagePaginationService.InvalidateMessageCachesAsync(
                        message.ChatId, token);

                await _messagePaginationService.InvalidateUserChatsCacheAsync(
                    message.ChatId, token);
            }
            catch
            {
                await transaction.RollbackAsync(token);
                throw;
            }
        }
    }
}
