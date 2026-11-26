using Messenger.Core.DTOs.Cache;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Services
{
    public class MessagePaginationService : IMessagePaginationService
    {
        private readonly GuapMessengerContext _context;
        private readonly ICacheService _cache;
        private readonly ILogger<MessagePaginationService> _logger;

        private static readonly TimeSpan LatestMessagesTtl = TimeSpan.FromSeconds(25);

        public MessagePaginationService(GuapMessengerContext context, ICacheService cache,
            ILogger<MessagePaginationService> logger)
        {
            _context = context;
            _cache = cache;
            _logger = logger;
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

        public async Task PrependMessageToCachesAsync(Message message, CancellationToken token = default)
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

        public async Task InvalidateUserChatsCacheAsync(Guid chatId, CancellationToken token = default)
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

        public Task InvalidateUserChatsCacheForUserAsync(Guid userId, CancellationToken token = default)
        {
            return _cache.RemoveAsync($"user:{userId:N}:chats", token);
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

        public async Task<(IReadOnlyList<Message> Items, bool HasMore)> GetMessagesByChatIdPagedAsync(Guid chatId,
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

    }
}
