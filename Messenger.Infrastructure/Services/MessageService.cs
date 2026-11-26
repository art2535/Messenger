using Messenger.Core.DTOs;
using Messenger.Core.DTOs.Messages;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using MassTransit;
using Messenger.Core.Messages;
using Messenger.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Services
{
    public class MessageService : IMessageService
    {
        private readonly GuapMessengerContext _context;
        private readonly IPublishEndpoint _publishEndpoint;
        private readonly IMessagePaginationService _messagePaginationService;
        private readonly ILogger<MessageService> _logger;
        private readonly IMessageExportService _messageExportService;
        private readonly IMessageSearchService _messageSearchService;

        public MessageService(GuapMessengerContext context,
            IPublishEndpoint publishEndpoint, IMessagePaginationService messagePaginationService, ILogger<MessageService> logger,
            IMessageExportService messageExportService,
            IMessageSearchService messageSearchService)
        {
            _context = context;
            _publishEndpoint = publishEndpoint;
            _messagePaginationService = messagePaginationService;
            _logger = logger;
            _messageExportService = messageExportService;
            _messageSearchService = messageSearchService;
        }

        public async Task PublishChatMessageAsync(ChatMessageSent message, CancellationToken cancellationToken = default)
        {
            await _publishEndpoint.Publish(message, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public Task<List<MessageDto>> SearchMessagesAsync(
            Guid chatId,
            string query,
            CancellationToken token = default)
        {
            return _messageSearchService.SearchMessagesAsync(chatId, query, token);
        }

        public async Task<int> MarkMessagesAsReadAsync(Guid chatId, Guid readerId, CancellationToken ct = default)
        {
           var now = UtcDateTime.Now;

            var updated = await _context.Messages
                .Where(m => m.ChatId == chatId
                         && m.SenderId != readerId
                         && m.ReadTime == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.ReadTime, now)
                    .SetProperty(m => m.DeliveryStatus, MessageDeliveryStatus.Read),
                ct);
            
            await _messagePaginationService.InvalidateUserChatsCacheForUserAsync(readerId, ct);
            _logger.LogInformation("MarkAsRead: chatId={ChatId} count={Count}, messages cache kept, user chats cache invalidated",
                chatId, updated);
            return updated;
        }

        public async Task<IEnumerable<Message>> GetMessagesAsync(Guid chatId, CancellationToken token = default)
        {
            var (items, _) = await _messagePaginationService.GetMessagesByChatIdPagedAsync(chatId, null, 100, token);
            return items;
        }

        public async Task<(IReadOnlyList<Message> Items, bool HasMore)> GetMessagesPagedAsync(
            Guid chatId, long? beforeSequence = null, int limit = 50, CancellationToken token = default)
        {
            return await _messagePaginationService.GetMessagesByChatIdPagedAsync(chatId, beforeSequence, limit, token);
        }

        public async Task<ServiceResult<Message>> SendMessageAsync(Guid messageId, Guid chatId, Guid senderId,
            string? content, bool hasAttachments, IFormFile[]? files = null, CancellationToken token = default)
        {
            try
            {
                var message = new Message
                {
                    MessageId = messageId,
                    ChatId = chatId,
                    SenderId = senderId,
                    MessageText = content ?? string.Empty,
                    HasAttachments = hasAttachments,
                    SendTime = UtcDateTime.Now,
                    DeliveryStatus = MessageDeliveryStatus.Pending
                };

                await AddMessageAsync(message, token);

                var savedMessage = await GetMessageByIdAsync(chatId, message.MessageId, token);

                return savedMessage != null
                    ? ServiceResult<Message>.Success(savedMessage)
                    : ServiceResult<Message>.Failure("Не удалось сохранить сообщение");
            }
            catch (Exception ex)
            {
                return ServiceResult<Message>.Failure(ex.Message, ex.InnerException?.Message);
            }
        }

        public async Task<Message?> GetMessageByIdAsync(Guid chatId, Guid messageId, CancellationToken token = default)
        {
            return await _context.Messages
                .FirstOrDefaultAsync(m => m.ChatId == chatId && m.MessageId == messageId, token);
        }

        public async Task<Message?> GetMessageByIdGlobalAsync(Guid messageId, CancellationToken token = default)
        {
            return await _context.Messages
                .Include(m => m.Attachments)
                .Include(m => m.Sender)
                .FirstOrDefaultAsync(m => m.MessageId == messageId, token);
        }

        public async Task DeleteMessageAsync(Guid messageId, CancellationToken token = default)
        {
            var deletedMessage = await _context.Messages.FindAsync(new object[] { messageId }, token);
            if (deletedMessage != null)
            {
                var chatId = deletedMessage.ChatId;
                _context.Messages.Remove(deletedMessage);
                await _context.SaveChangesAsync(token);
                await _messagePaginationService.InvalidateMessageCachesAsync(chatId, token);
                await _messagePaginationService.InvalidateUserChatsCacheAsync(chatId, token);
            }
        }

        public async Task UpdateMessageAsync(Message message, CancellationToken token = default)
        {
            _context.Messages.Update(message);
            await _context.SaveChangesAsync(token);
            await _messagePaginationService.InvalidateMessageCachesAsync(message.ChatId, token);
        }

        public async Task<ChatExportResult> ExportChatAsync(Guid chatId, string format, string? chatName = null, 
            CancellationToken token = default)
        {
            format = (format ?? "txt").Trim().ToLowerInvariant();
            if (format is not ("txt" or "json" or "html" or "csv"))
                format = "txt";

            var allMessages = new List<Message>();
            long? beforeSeq = null;
            const int pageSize = 200;
            const int maxMessages = 5000;
            bool hasMore = true;

            while (hasMore && allMessages.Count < maxMessages)
            {
                var (batch, more) = await _messagePaginationService.GetMessagesByChatIdPagedAsync(chatId, beforeSeq, pageSize, token);
                if (batch.Count == 0)
                    break;

                allMessages.InsertRange(0, batch);
                beforeSeq = batch.Min(m => m.SequenceNumber);
                hasMore = more;

                if (batch.Count < pageSize)
                    hasMore = false;
            }

            allMessages = allMessages.OrderBy(m => m.SequenceNumber).ToList();

            return _messageExportService.Export(chatId, format, chatName, allMessages);
        }

        public async Task<int> DeleteMessagesAsync(IEnumerable<Guid> messageIds, CancellationToken token = default)
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

        private async Task AddMessageAsync(Message message, CancellationToken token = default)
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
                    await _messagePaginationService.InvalidateMessageCachesAsync(message.ChatId, token);

                await _messagePaginationService.InvalidateUserChatsCacheAsync(message.ChatId, token);
            }
            catch
            {
                await transaction.RollbackAsync(token);
                throw;
            }
        }

        public async Task<Message?> PinMessageAsync(Guid messageId, Guid userId, CancellationToken token = default)
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
            message.PinnedAt = Messenger.Core.Models.UtcDateTime.Now;
            await _context.SaveChangesAsync(token);
            await _messagePaginationService.InvalidateMessageCachesAsync(message.ChatId, token);
            return message;
        }

        public async Task<Message?> UnpinMessageAsync(Guid messageId, Guid userId, CancellationToken token = default)
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

        public async Task<List<Message>> GetPinnedMessagesAsync(Guid chatId, CancellationToken token = default)
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