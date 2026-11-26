using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Services
{
    public class MessageMutationService : IMessageMutationService
    {
        private readonly GuapMessengerContext _context;
        private readonly IMessagePaginationService _messagePaginationService;

        public MessageMutationService(
            GuapMessengerContext context,
            IMessagePaginationService messagePaginationService)
        {
            _context = context;
            _messagePaginationService = messagePaginationService;
        }

        public async Task DeleteMessageAsync(
            Guid messageId,
            CancellationToken token = default)
        {
            var deletedMessage = await _context.Messages.FindAsync(
                new object[] { messageId },
                token);

            if (deletedMessage != null)
            {
                var chatId = deletedMessage.ChatId;
                _context.Messages.Remove(deletedMessage);
                await _context.SaveChangesAsync(token);
                await _messagePaginationService.InvalidateMessageCachesAsync(chatId, token);
                await _messagePaginationService.InvalidateUserChatsCacheAsync(chatId, token);
            }
        }

        public async Task UpdateMessageAsync(
            Message message,
            CancellationToken token = default)
        {
            _context.Messages.Update(message);
            await _context.SaveChangesAsync(token);
            await _messagePaginationService.InvalidateMessageCachesAsync(message.ChatId, token);
        }

        public async Task<int> DeleteMessagesAsync(
            IEnumerable<Guid> messageIds,
            CancellationToken token = default)
        {
            var ids = messageIds?.Distinct().ToList() ?? new List<Guid>();
            if (ids.Count == 0)
                return 0;

            var messages = await _context.Messages
                .Where(m => ids.Contains(m.MessageId))
                .ToListAsync(token);

            if (messages.Count == 0)
                return 0;

            var chatIds = messages.Select(m => m.ChatId).Distinct().ToList();
            _context.Messages.RemoveRange(messages);
            await _context.SaveChangesAsync(token);

            foreach (var chatId in chatIds)
            {
                await _messagePaginationService.InvalidateMessageCachesAsync(chatId, token);
                await _messagePaginationService.InvalidateUserChatsCacheAsync(chatId, token);
            }

            return messages.Count;
        }
    }
}
