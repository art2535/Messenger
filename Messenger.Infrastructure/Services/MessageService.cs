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
using Messenger.Core.DTOs.Cache;

namespace Messenger.Infrastructure.Services
{
    public class MessageService : IMessageService
    {
        private readonly GuapMessengerContext _context;
        private readonly IEncryptionService _encryptionService;
        private readonly IPublishEndpoint _publishEndpoint;
        private readonly ICacheService _cache;
        private readonly ILogger<MessageService> _logger;
        private readonly IMessageExportService _messageExportService;

        private static readonly TimeSpan LatestMessagesTtl = TimeSpan.FromSeconds(25);

        private const int SearchScanLimit = 300;

        public MessageService(GuapMessengerContext context, IEncryptionService encryptionService,
            IPublishEndpoint publishEndpoint, ICacheService cache, ILogger<MessageService> logger,
            IMessageExportService messageExportService)
        {
            _context = context;
            _encryptionService = encryptionService;
            _publishEndpoint = publishEndpoint;
            _cache = cache;
            _logger = logger;
            _messageExportService = messageExportService;
        }

        private static string LatestMessagesCacheKey(Guid chatId, int limit)
            => $"chat:{chatId:N}:messages:latest:{limit}";

        public async Task InvalidateMessageCachesAsync(Guid chatId, CancellationToken token = default)
        {
            var keys = new[] { 1, 20, 30, 50, 100, 200 }
                .Select(limit => LatestMessagesCacheKey(chatId, limit))
                .ToArray();
            await _cache.RemoveAsync(keys, token);
            _logger.LogDebug("Messages cache invalidated chatId={ChatId}", chatId);
        }

        private async Task PrependMessageToCachesAsync(Message message, CancellationToken token = default)
        {
            var item = ToCacheItem(message);
            var limits = new[] { 1, 20, 30, 50, 100, 200 };

            foreach (var limit in limits)
            {
                var key = LatestMessagesCacheKey(message.ChatId, limit);
                var page = await _cache.GetAsync<CachedMessagesPage>(key, token);
                if (page is null || page.Items is null)
                    continue;

                if (page.Items.Any(x => x.MessageId == item.MessageId))
                    continue;

                page.Items.Add(item);
                if (page.Items.Count > limit)
                {
                    page.Items = page.Items
                        .OrderBy(x => x.SequenceNumber)
                        .TakeLast(limit)
                        .ToList();
                    page.HasMore = true;
                }
                else
                {
                    page.Items = page.Items.OrderBy(x => x.SequenceNumber).ToList();
                }

                await _cache.SetAsync(key, page, LatestMessagesTtl, token);
            }

            _logger.LogDebug("Messages cache prepended chatId={ChatId} messageId={MessageId}",
                message.ChatId, message.MessageId);
        }

        private async Task InvalidateUserChatsCacheAsync(Guid chatId, CancellationToken token = default)
        {
            var userIds = await _context.ChatParticipants
                .AsNoTracking()
                .Where(p => p.ChatId == chatId)
                .Select(p => p.UserId)
                .ToListAsync(token);

            if (userIds.Count == 0)
                return;

            await _cache.RemoveAsync(userIds.Select(id => $"user:{id:N}:chats"), token);
        }

        public async Task PublishChatMessageAsync(ChatMessageSent message, CancellationToken cancellationToken = default)
        {
            await _publishEndpoint.Publish(message, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<List<MessageDto>> SearchMessagesAsync(Guid chatId, string query, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<MessageDto>();

            query = query.Trim().ToLowerInvariant();

            var messages = await _context.Messages
                .AsNoTracking()
                .Where(m => m.ChatId == chatId)
                .OrderByDescending(m => m.SequenceNumber)
                .Take(SearchScanLimit)
                .Include(m => m.Sender)
                .Include(m => m.Reactions)
                    .ThenInclude(r => r.User)
                .Include(m => m.Attachments)
                .AsSplitQuery()
                .ToListAsync(token);

            messages.Reverse();

            var filtered = new List<MessageDto>();

            foreach (var m in messages)
            {
                string decryptedText = _encryptionService.TryDecryptSafe(m.MessageText);

                if (decryptedText.ToLowerInvariant().Contains(query))
                {
                    filtered.Add(MapToDto(m, decryptedText));
                }
            }

            return filtered.OrderBy(m => m.SentAt).ToList();
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
            
            await _cache.RemoveAsync($"user:{readerId:N}:chats", ct);
            _logger.LogInformation("MarkAsRead: chatId={ChatId} count={Count}, messages cache kept, user chats cache invalidated",
                chatId, updated);
            return updated;
        }

        public async Task<IEnumerable<Message>> GetMessagesAsync(Guid chatId, CancellationToken token = default)
        {
            var (items, _) = await GetMessagesByChatIdPagedAsync(chatId, null, 100, token);
            return items;
        }

        public async Task<(IReadOnlyList<Message> Items, bool HasMore)> GetMessagesPagedAsync(
            Guid chatId, long? beforeSequence = null, int limit = 50, CancellationToken token = default)
        {
            return await GetMessagesByChatIdPagedAsync(chatId, beforeSequence, limit, token);
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
                await InvalidateMessageCachesAsync(chatId, token);
                await InvalidateUserChatsCacheAsync(chatId, token);
            }
        }

        public async Task UpdateMessageAsync(Message message, CancellationToken token = default)
        {
            _context.Messages.Update(message);
            await _context.SaveChangesAsync(token);
            await InvalidateMessageCachesAsync(message.ChatId, token);
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
                var (batch, more) = await GetMessagesByChatIdPagedAsync(chatId, beforeSeq, pageSize, token);
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
                await InvalidateMessageCachesAsync(chatId, token);
                await InvalidateUserChatsCacheAsync(chatId, token);
            }
            return messages.Count;
        }

        private static MessageDto MapToDto(Message m, string decryptedText)
        {
            var reactionGroups = (m.Reactions ?? Enumerable.Empty<Reaction>())
                .GroupBy(r => r.ReactionType)
                .Select(g => new ReactionSummaryDto
                {
                    ReactionType = g.Key,
                    Count = g.Count(),
                    Users = g.Select(r => new ReactionUserDto
                    {
                        UserId = r.UserId,
                        UserName = r.User != null
                            ? $"{r.User.FirstName} {r.User.LastName}".Trim()
                            : null
                    }).ToList()
                })
                .OrderByDescending(x => x.Count)
                .ToList();

            return new MessageDto
            {
                MessageId = m.MessageId,
                ChatId = m.ChatId,
                SenderId = m.SenderId,
                SenderName = m.Sender != null
                    ? $"{m.Sender.FirstName} {m.Sender.LastName}".Trim()
                    : "Пользователь",
                MessageText = decryptedText,
                SentAt = Messenger.Core.Models.UtcDateTime.AsUtc(m.SendTime),
                SequenceNumber = m.SequenceNumber,
                Status = m.DeliveryStatus.ToString(),
                Attachments = m.Attachments?.Select(a => new AttachmentDto
                {
                    AttachmentId = a.AttachmentId,
                    FileName = a.FileName,
                    FileType = a.FileType,
                    SizeInBytes = a.SizeInBytes ?? 0,
                    Url = a.Url
                }).ToList() ?? new List<AttachmentDto>(),
                Reactions = reactionGroups,
                IsPinned = m.IsPinned,
                PinnedAt = m.PinnedAt.HasValue
                    ? Messenger.Core.Models.UtcDateTime.AsUtc(m.PinnedAt.Value)
                    : null
            };
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
                    await PrependMessageToCachesAsync(forCache, token);
                else
                    await InvalidateMessageCachesAsync(message.ChatId, token);

                await InvalidateUserChatsCacheAsync(message.ChatId, token);
            }
            catch
            {
                await transaction.RollbackAsync(token);
                throw;
            }
        }

        private async Task<(IReadOnlyList<Message> Items, bool HasMore)> GetMessagesByChatIdPagedAsync(Guid chatId,
            long? beforeSequence = null, int limit = 50, CancellationToken token = default)
        {
            if (limit <= 0)
                limit = 50;
            if (limit > 200)
                limit = 200;

            if (beforeSequence is null)
            {
                var cacheKey = LatestMessagesCacheKey(chatId, limit);
                var cached = await _cache.GetAsync<CachedMessagesPage>(cacheKey, token);
                if (cached is not null && cached.Items is { Count: > 0 })
                {
                    var fromCache = cached.Items.Select(ToMessage).ToList();
                    _logger.LogDebug(
                        "GetMessages: source=Redis chatId={ChatId} limit={Limit} count={Count}",
                        chatId, limit, fromCache.Count);
                    return (fromCache, cached.HasMore);
                }

                _logger.LogDebug(
                    "GetMessages: source=PostgreSQL chatId={ChatId} limit={Limit} (cache miss)",
                    chatId, limit);
            }
            else
            {
                _logger.LogDebug(
                    "GetMessages: source=PostgreSQL chatId={ChatId} beforeSeq={Before} limit={Limit} (cursor page, без кэша)",
                    chatId, beforeSequence, limit);
            }

            var query = _context.Messages
                .AsNoTracking()
                .Where(m => m.ChatId == chatId);

            if (beforeSequence.HasValue)
                query = query.Where(m => m.SequenceNumber < beforeSequence.Value);

            var batch = await query
                .OrderByDescending(m => m.SequenceNumber)
                .Take(limit + 1)
                .Include(m => m.Sender)
                .Include(m => m.Reactions)
                    .ThenInclude(r => r.User)
                .Include(m => m.Attachments)
                .AsSplitQuery()
                .ToListAsync(token);

            var hasMore = batch.Count > limit;
            if (hasMore)
                batch = batch.Take(limit).ToList();

            batch.Reverse();

            if (beforeSequence is null)
            {
                await _cache.SetAsync(
                    LatestMessagesCacheKey(chatId, limit),
                    new CachedMessagesPage
                    {
                        Items = batch.Select(ToCacheItem).ToList(),
                        HasMore = hasMore
                    },
                    LatestMessagesTtl,
                    token);
            }

            return (batch, hasMore);
        }

        private static CachedMessageItem ToCacheItem(Message m) => new()
        {
            MessageId = m.MessageId,
            ChatId = m.ChatId,
            SenderId = m.SenderId,
            SenderFirstName = m.Sender?.FirstName,
            SenderLastName = m.Sender?.LastName,
            MessageText = m.MessageText ?? string.Empty,
            SendTime = m.SendTime,
            SequenceNumber = m.SequenceNumber,
            DeliveryStatus = m.DeliveryStatus,
            ReadTime = m.ReadTime,
            HasAttachments = m.HasAttachments,
            IsPinned = m.IsPinned,
            PinnedAt = m.PinnedAt,
            Attachments = (m.Attachments ?? Enumerable.Empty<Attachment>())
                .Select(a => new CachedAttachmentItem
                {
                    AttachmentId = a.AttachmentId,
                    FileName = a.FileName,
                    FileType = a.FileType,
                    SizeInBytes = a.SizeInBytes,
                    Url = a.Url
                }).ToList(),
            Reactions = (m.Reactions ?? Enumerable.Empty<Reaction>())
                .Select(r => new CachedReactionItem
                {
                    ReactionType = r.ReactionType,
                    UserId = r.UserId,
                    UserFirstName = r.User?.FirstName,
                    UserLastName = r.User?.LastName
                }).ToList()
        };

        private static Message ToMessage(CachedMessageItem c) => new()
        {
            MessageId = c.MessageId,
            ChatId = c.ChatId,
            SenderId = c.SenderId,
            MessageText = c.MessageText,
            SendTime = c.SendTime,
            SequenceNumber = c.SequenceNumber,
            DeliveryStatus = c.DeliveryStatus,
            ReadTime = c.ReadTime,
            HasAttachments = c.HasAttachments,
            IsPinned = c.IsPinned,
            PinnedAt = c.PinnedAt,
            Sender = new User
            {
                UserId = c.SenderId,
                FirstName = c.SenderFirstName ?? string.Empty,
                LastName = c.SenderLastName ?? string.Empty
            },
            Attachments = c.Attachments.Select(a => new Attachment
            {
                AttachmentId = a.AttachmentId,
                FileName = a.FileName ?? string.Empty,
                FileType = a.FileType,
                SizeInBytes = a.SizeInBytes,
                Url = a.Url ?? string.Empty
            }).ToList(),
            Reactions = c.Reactions.Select(r => new Reaction
            {
                ReactionType = r.ReactionType ?? string.Empty,
                UserId = r.UserId,
                User = new User
                {
                    UserId = r.UserId,
                    FirstName = r.UserFirstName ?? string.Empty,
                    LastName = r.UserLastName ?? string.Empty
                }
            }).ToList()
        };

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
            await InvalidateMessageCachesAsync(message.ChatId, token);
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
            await InvalidateMessageCachesAsync(message.ChatId, token);
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