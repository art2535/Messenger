using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Services
{
    public class MessageLookupService : IMessageLookupService
    {
        private readonly GuapMessengerContext _context;

        public MessageLookupService(GuapMessengerContext context)
        {
            _context = context;
        }

        public Task<Message?> GetMessageByIdAsync(
            Guid chatId,
            Guid messageId,
            CancellationToken token = default)
        {
            return _context.Messages
                .FirstOrDefaultAsync(
                    message => message.ChatId == chatId && message.MessageId == messageId,
                    token);
        }

        public Task<Message?> GetMessageByIdGlobalAsync(
            Guid messageId,
            CancellationToken token = default)
        {
            return _context.Messages
                .Include(message => message.Attachments)
                .Include(message => message.Sender)
                .FirstOrDefaultAsync(message => message.MessageId == messageId, token);
        }
    }
}
