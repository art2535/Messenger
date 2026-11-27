using Messenger.Application.DTOs.Chats;
using Messenger.Application.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Services
{
    public class ChatService : IChatService
    {
        private readonly GuapMessengerContext _context;
        private readonly IEncryptionService _encryptionService;
        private readonly ICacheService _cache;
        private readonly ILogger<ChatService> _logger;

        private static readonly TimeSpan ChatsListTtl = TimeSpan.FromSeconds(45);

        public ChatService(GuapMessengerContext context, IEncryptionService encryptionService, 
            ICacheService cache, ILogger<ChatService> logger)
        {
            _context = context;
            _encryptionService = encryptionService;
            _cache = cache;
            _logger = logger;
        }

        private static string UserChatsCacheKey(Guid userId) => $"user:{userId:N}:chats";

        private static ChatDto MapChat(Chat chat)
        {
            return new ChatDto
            {
                ChatId = chat.ChatId,
                Name = chat.Name,
                Type = chat.Type,
                UserId = chat.UserId,
                CreationDate = chat.CreationDate,
                Participants = chat.ChatParticipants.Select(MapParticipant).ToList()
            };
        }

        private static ChatParticipantDto MapParticipant(ChatParticipant participant)
        {
            return new ChatParticipantDto
            {
                ChatId = participant.ChatId,
                UserId = participant.UserId,
                Role = participant.Role,
                JoinDate = participant.JoinDate,
                FirstName = participant.User?.FirstName,
                LastName = participant.User?.LastName,
                Avatar = participant.User?.Account?.Avatar,
                ExternalId = participant.User?.ExternalId
            };
        }

        public async Task<ChatDto> CreateChatAsync(string name, string type, Guid creatorId, CancellationToken token = default)
        {
            var chat = new Chat
            {
                ChatId = Guid.NewGuid(),
                Name = name,
                Type = type,
                UserId = creatorId,
                CreationDate = UtcDateTime.Now
            };

            var participant = new ChatParticipant
            {
                ChatId = chat.ChatId,
                UserId = creatorId,
                Role = "владелец",
                JoinDate = UtcDateTime.Now
            };

            await _context.Chats.AddAsync(chat, token);
            await _context.SaveChangesAsync(token);
            await _context.ChatParticipants.AddAsync(participant, token);
            await _context.SaveChangesAsync(token);

            await _cache.RemoveAsync(UserChatsCacheKey(creatorId), token);

            return new ChatDto { ChatId = chat.ChatId, Name = chat.Name, Type = chat.Type, UserId = chat.UserId, CreationDate = chat.CreationDate };
        }

        public async Task<List<ChatListItemDto>> GetUserChatsWithLastMessageAsync(Guid userId, CancellationToken token = default)
        {
            var cacheKey = UserChatsCacheKey(userId);
            var cached = await _cache.GetAsync<List<ChatListItemDto>>(cacheKey, token);
            if (cached is { Count: > 0 })
            {
                _logger.LogInformation("GetUserChats: source=Redis userId={UserId} chats={Count}", userId, cached.Count);
                return cached;
            }

            _logger.LogInformation("GetUserChats: source=PostgreSQL userId={UserId} (cache miss)", userId);

            var chats = await _context.Chats
                .AsNoTracking()
                .Include(c => c.ChatParticipants)
                    .ThenInclude(cp => cp.User)
                        .ThenInclude(u => u.Account)
                .Where(c => c.ChatParticipants.Any(cp => cp.UserId == userId))
                .ToListAsync(token);

            if (chats.Count == 0)
            {
                await _cache.SetAsync(cacheKey, new List<ChatListItemDto>(), ChatsListTtl, token);
                return new List<ChatListItemDto>();
            }

            var chatIds = chats.Select(c => c.ChatId).ToList();

            var lastMessagesRaw = await _context.Messages
                .AsNoTracking()
                .Where(m => chatIds.Contains(m.ChatId))
                .GroupBy(m => m.ChatId)
                .Select(g => new
                {
                    ChatId = g.Key,
                    MaxSeq = g.Max(x => x.SequenceNumber)
                })
                .ToListAsync(token);

            var maxSeqByChat = lastMessagesRaw.ToDictionary(x => x.ChatId, x => x.MaxSeq);

            List<Message> lastMessages = new();
            if (maxSeqByChat.Count > 0)
            {
                var predicates = maxSeqByChat.Select(kv =>
                    _context.Messages.AsNoTracking()
                        .Where(m => m.ChatId == kv.Key && m.SequenceNumber == kv.Value)
                        .Include(m => m.Sender)
                        .Include(m => m.Attachments));

                var chatIdSeqPairs = maxSeqByChat.Select(kv => (kv.Key, kv.Value)).ToList();

                lastMessages = await _context.Messages
                    .AsNoTracking()
                    .Where(m => chatIds.Contains(m.ChatId))
                    .Where(m => maxSeqByChat.Keys.Contains(m.ChatId)) // filter early
                    .Include(m => m.Sender)
                    .Include(m => m.Attachments)
                    .ToListAsync(token);

                lastMessages = lastMessages
                    .GroupBy(m => m.ChatId)
                    .Select(g => g.OrderByDescending(m => m.SequenceNumber).First())
                    .ToList();
            }

            var lastMsgByChat = lastMessages.ToDictionary(m => m.ChatId);

            var unreadCounts = await _context.Messages
                .AsNoTracking()
                .Where(m => chatIds.Contains(m.ChatId)
                         && m.SenderId != userId
                         && m.ReadTime == null)
                .GroupBy(m => m.ChatId)
                .Select(g => new { ChatId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ChatId, x => x.Count, token);

            var sortable = new List<(DateTime SortKey, ChatListItemDto Item)>();

            foreach (var chat in chats)
            {
                lastMsgByChat.TryGetValue(chat.ChatId, out var lastMsg);
                DateTime? lastMessageAt = lastMsg != null
                    ? UtcDateTime.AsUtc(lastMsg.SendTime)
                    : UtcDateTime.AsUtc(chat.CreationDate);
                if (lastMessageAt == DateTime.MinValue)
                    lastMessageAt = null;

                string? decryptedLastMessage = null;
                if (lastMsg?.MessageText != null)
                {
                    try
                    {
                        decryptedLastMessage = _encryptionService.Decrypt(lastMsg.MessageText);
                    }
                    catch
                    {
                        decryptedLastMessage = "[Сообщение защищено]";
                    }
                }
                else if (lastMsg != null && lastMsg.HasAttachments)
                {
                    decryptedLastMessage = "Вложение";
                }

                unreadCounts.TryGetValue(chat.ChatId, out var unreadCount);

                var sortKey = lastMessageAt ?? DateTime.MinValue;

                var item = new ChatListItemDto
                {
                    ChatId = chat.ChatId,
                    Name = chat.Type == "private"
                        ? chat.ChatParticipants
                            .Where(p => p.UserId != userId)
                            .Select(p => $"{p.User.FirstName} {p.User.LastName}".Trim())
                            .FirstOrDefault() ?? "Приватный чат"
                        : chat.Name,
                    Avatar = chat.Type == "private"
                        ? chat.ChatParticipants
                            .Where(p => p.UserId != userId)
                            .Select(p => p.User.Account?.Avatar)
                            .FirstOrDefault()
                        : null,
                    Type = chat.Type,
                    LastMessage = decryptedLastMessage,
                    LastMessageAt = lastMessageAt,
                    LastMessageTime = lastMessageAt,
                    LastMessageSentAt = lastMessageAt,
                    IsOnline = true,
                    UnreadCount = unreadCount
                };

                sortable.Add((sortKey, item));
            }

            var result = sortable
                .OrderByDescending(x => x.SortKey)
                .Select(x => x.Item)
                .ToList();

            await _cache.SetAsync(cacheKey, result, ChatsListTtl, token);
            return result;
        }

        public async Task<IEnumerable<ChatDto>> GetUserChatsAsync(Guid userId, CancellationToken token = default)
        {
            var chats = await _context.Chats
                .AsNoTracking()
                .Include(c => c.ChatParticipants)
                    .ThenInclude(cp => cp.User)
                        .ThenInclude(u => u.Account)
                .Where(c => c.ChatParticipants.Any(cp => cp.UserId == userId))
                .ToListAsync(token);

            return chats.Select(MapChat);
        }

        public async Task AddParticipantToChatAsync(Guid chatId, Guid userId, string role, CancellationToken token = default)
        {
            var existing = await _context.ChatParticipants.FirstOrDefaultAsync(part => part.ChatId == chatId && part.UserId == userId, token);
            if (existing != null)
                return;

            var participant = new ChatParticipant
            {
                ChatId = chatId,
                UserId = userId,
                Role = role,
                JoinDate = UtcDateTime.Now
            };

            try
            {
                await _context.ChatParticipants.AddAsync(participant, token);
                await _context.SaveChangesAsync(token);
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("duplicate key") == true)
            {
                return;
            }

            await InvalidateChatsCacheForParticipantsAsync(chatId, token);
        }

        public async Task DeleteParticipantFromChatAsync(Guid chatId, Guid userId, CancellationToken token = default)
        {
            var deleteParticipant = await _context.ChatParticipants.FirstOrDefaultAsync(part => part.ChatId == chatId && part.UserId == userId, token);

            if (deleteParticipant != null)
            {
                _context.ChatParticipants.Remove(deleteParticipant);
                await _context.SaveChangesAsync(token);
            }

            await _cache.RemoveAsync(UserChatsCacheKey(userId), token);
            await InvalidateChatsCacheForParticipantsAsync(chatId, token);
        }

        public async Task DeleteChatAsync(Guid chatId, CancellationToken token = default)
        {
            var participantIds = await _context.ChatParticipants
                .Where(p => p.ChatId == chatId)
                .Select(p => p.UserId)
                .ToListAsync(token);

            var chat = await _context.Chats.FirstOrDefaultAsync(c => c.ChatId == chatId, token);
            if (chat == null)
                return;

            _context.Chats.Remove(chat);
            await _context.SaveChangesAsync(token);

            var keys = participantIds.Select(UserChatsCacheKey);
            await _cache.RemoveAsync(keys, token);
        }

        public async Task<ChatDto?> GetChatByIdAsync(Guid chatId, CancellationToken token = default)
        {
            var chat = await _context.Chats
                .AsNoTracking()
                .Include(c => c.ChatParticipants)
                    .ThenInclude(cp => cp.User)
                        .ThenInclude(u => u.Account)
                .FirstOrDefaultAsync(c => c.ChatId == chatId, token);

            return chat == null ? null : MapChat(chat);
        }

        public async Task<IEnumerable<ChatParticipantDto>> GetChatParticipantsAsync(Guid chatId,
            CancellationToken token = default)
        {
            var participants = await _context.ChatParticipants
                .AsNoTracking()
                .Where(part => part.ChatId == chatId)
                .Include(p => p.User)
                    .ThenInclude(u => u.Account)
                .ToListAsync(token);

            return participants.Select(MapParticipant);
        }

        public async Task UpdateChatAsync(UpdateChatDto chat, CancellationToken token = default)
        {
            var entity = await _context.Chats.FirstOrDefaultAsync(c => c.ChatId == chat.ChatId, token);
            if (entity == null)
                return;

            entity.Name = chat.Name;
            await _context.SaveChangesAsync(token);
            await InvalidateChatsCacheForParticipantsAsync(chat.ChatId, token);
        }

        public async Task InvalidateChatsCacheForParticipantsAsync(Guid chatId, CancellationToken token = default)
        {
            var userIds = await _context.ChatParticipants
                .AsNoTracking()
                .Where(p => p.ChatId == chatId)
                .Select(p => p.UserId)
                .ToListAsync(token);

            if (userIds.Count == 0) return;

            await _cache.RemoveAsync(userIds.Select(UserChatsCacheKey), token);
        }

        public async Task<IReadOnlyList<Guid>> GetPeerUserIdsAsync(Guid userId, CancellationToken token = default)
        {
            var chatIds = await _context.ChatParticipants
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .Select(p => p.ChatId)
                .ToListAsync(token);

            if (chatIds.Count == 0)
                return new[] { userId };

            var peerIds = await _context.ChatParticipants
                .AsNoTracking()
                .Where(p => chatIds.Contains(p.ChatId))
                .Select(p => p.UserId)
                .Distinct()
                .ToListAsync(token);

            if (!peerIds.Contains(userId))
                peerIds.Add(userId);

            return peerIds;
        }

        public async Task<IReadOnlyList<Guid>> GetChatParticipantUserIdsAsync(Guid chatId, CancellationToken token = default)
        {
            return await _context.ChatParticipants
                .AsNoTracking()
                .Where(p => p.ChatId == chatId)
                .Select(p => p.UserId)
                .ToListAsync(token);
        }
    }
}
