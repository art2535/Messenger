using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Services
{
    public class MessageReadService : IMessageReadService
    {
        private readonly GuapMessengerContext _context;
        private readonly IMessagePaginationService _messagePaginationService;
        private readonly ILogger<MessageReadService> _logger;

        public MessageReadService(
            GuapMessengerContext context,
            IMessagePaginationService messagePaginationService,
            ILogger<MessageReadService> logger)
        {
            _context = context;
            _messagePaginationService = messagePaginationService;
            _logger = logger;
        }

        public async Task<int> MarkMessagesAsReadAsync(
            Guid chatId,
            Guid readerId,
            CancellationToken token = default)
        {
            var now = UtcDateTime.Now;

            var updated = await _context.Messages
                .Where(m => m.ChatId == chatId
                         && m.SenderId != readerId
                         && m.ReadTime == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.ReadTime, now)
                    .SetProperty(m => m.DeliveryStatus, MessageDeliveryStatus.Read),
                token);

            await _messagePaginationService.InvalidateUserChatsCacheForUserAsync(readerId, token);
            _logger.LogInformation(
                "MarkAsRead: chatId={ChatId} count={Count}, messages cache kept, user chats cache invalidated",
                chatId, updated);

            return updated;
        }
    }
}
