using Messenger.Application.Common;
using Messenger.Application.DTOs.Chats;
using Messenger.Application.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

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

        private async Task<bool> IsChatParticipantAsync(Guid chatId, Guid userId, CancellationToken token)
        {
            if (userId == Guid.Empty)
                return false;

            return await _context.ChatParticipants.AsNoTracking()
                .AnyAsync(p => p.ChatId == chatId && p.UserId == userId, token);
        }

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

            _context.Chats.Add(chat);
            _context.ChatParticipants.Add(participant);

            // Чат и его владелец сохраняются одной операцией.
            // Для реляционного провайдера EF Core SaveChanges выполняется транзакционно.
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

            // Получаем только последние сообщения каждого чата на стороне БД.
            // Не загружаем всю историю сообщений в память.
            var lastMessages = await _context.Messages
                .AsNoTracking()
                .Where(m => chatIds.Contains(m.ChatId))
                .Where(m => m.SequenceNumber == _context.Messages
                    .Where(candidate => candidate.ChatId == m.ChatId)
                    .Max(candidate => candidate.SequenceNumber))
                .Include(m => m.Sender)
                .Include(m => m.Attachments)
                .ToListAsync(token);

            var lastMsgByChat = lastMessages
                .GroupBy(m => m.ChatId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.SequenceNumber).First());

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

        public async Task AddParticipantToChatAsync(Guid chatId, Guid actorId, Guid userId, string role, CancellationToken token = default)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(token);
            var actor = await _context.ChatParticipants.AsNoTracking().FirstOrDefaultAsync(p => p.ChatId == chatId && p.UserId == actorId, token);
            if (actor == null || !IsAdministratorRole(actor.Role))
                throw new UnauthorizedAccessException("Недостаточно прав для добавления участника");
            if (!IsRegularParticipantRole(role))
                throw new InvalidOperationException("Через этот метод можно назначить только роль участника");

            var existing = await _context.ChatParticipants.FirstOrDefaultAsync(p => p.ChatId == chatId && p.UserId == userId, token);
            if (existing != null)
            {
                await transaction.CommitAsync(token);
                return;
            }

            var participant = new ChatParticipant { ChatId = chatId, UserId = userId, Role = "участник", JoinDate = UtcDateTime.Now };
            try
            {
                await _context.ChatParticipants.AddAsync(participant, token);
                await _context.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                _context.Entry(participant).State = EntityState.Detached;
                await transaction.RollbackAsync(token);
                return;
            }

            await InvalidateChatsCacheForParticipantsBestEffortAsync(chatId, token);
        }

        public async Task DeleteParticipantFromChatAsync(Guid chatId, Guid actorId, Guid userId, CancellationToken token = default)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(token);
            var actor = await _context.ChatParticipants.AsNoTracking().FirstOrDefaultAsync(p => p.ChatId == chatId && p.UserId == actorId, token);
            var target = await _context.ChatParticipants.FirstOrDefaultAsync(p => p.ChatId == chatId && p.UserId == userId, token);

            if (actor == null)
                throw new UnauthorizedAccessException("Вы не участник этого чата");
            if (!IsAdministratorRole(actor.Role))
                throw new UnauthorizedAccessException("Недостаточно прав для удаления участника");
            if (target == null)
                return;
            if (userId == actorId)
                throw new InvalidOperationException("Нельзя удалить самого себя из чата этим методом");
            if (IsAdministratorRole(target.Role))
                throw new UnauthorizedAccessException("Нельзя удалить владельца или администратора чата");

            _context.ChatParticipants.Remove(target);
            await _context.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            await InvalidateUserChatsCacheBestEffortAsync(userId, token);
            await InvalidateChatsCacheForParticipantsBestEffortAsync(chatId, token);
        }

        public async Task DeleteChatAsync(Guid chatId, Guid actorId, CancellationToken token = default)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(token);
            var chat = await _context.Chats.FirstOrDefaultAsync(c => c.ChatId == chatId, token);
            if (chat == null) return;

            var actor = await _context.ChatParticipants.AsNoTracking().FirstOrDefaultAsync(p => p.ChatId == chatId && p.UserId == actorId, token);
            if (actor == null || !IsOwnerRole(actor.Role) || actor.UserId != chat.UserId)
                throw new UnauthorizedAccessException("Только владелец может удалить чат");

            var participantIds = await _context.ChatParticipants.Where(p => p.ChatId == chatId).Select(p => p.UserId).ToListAsync(token);
            _context.Chats.Remove(chat);
            await _context.SaveChangesAsync(token);
            await transaction.CommitAsync(token);

            try { await _cache.RemoveAsync(participantIds.Select(UserChatsCacheKey), token); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Чат {ChatId} удалён, но кеш списков чатов не удалось инвалидировать", chatId);
            }
        }

        public async Task<ChatDto?> GetChatByIdAsync(Guid chatId, CancellationToken token = default)
        {
            return await GetChatByIdAsync(chatId, Guid.Empty, token, false);
        }

        public async Task<ChatDto?> GetChatByIdAsync(Guid chatId, Guid actorId, CancellationToken token = default)
        {
            return await GetChatByIdAsync(chatId, actorId, token, true);
        }

        private async Task<ChatDto?> GetChatByIdAsync(Guid chatId, Guid actorId, CancellationToken token, bool requireAuthorization)
        {
            if (requireAuthorization && !await IsChatParticipantAsync(chatId, actorId, token))
                throw new UnauthorizedAccessException("Вы не участник этого чата");

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
            return await GetChatParticipantsAsync(chatId, Guid.Empty, token, false);
        }

        public async Task<IEnumerable<ChatParticipantDto>> GetChatParticipantsAsync(Guid chatId, Guid actorId,
            CancellationToken token = default)
        {
            return await GetChatParticipantsAsync(chatId, actorId, token, true);
        }

        private async Task<IEnumerable<ChatParticipantDto>> GetChatParticipantsAsync(Guid chatId, Guid actorId,
            CancellationToken token, bool requireAuthorization)
        {
            if (requireAuthorization && !await IsChatParticipantAsync(chatId, actorId, token))
                throw new UnauthorizedAccessException("Вы не участник этого чата");

            var participants = await _context.ChatParticipants
                .AsNoTracking()
                .Where(part => part.ChatId == chatId)
                .Include(p => p.User)
                    .ThenInclude(u => u.Account)
                .ToListAsync(token);

            return participants.Select(MapParticipant);
        }

        public async Task UpdateChatAsync(UpdateChatDto chat, Guid actorId, CancellationToken token = default)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(token);
            var entity = await _context.Chats.FirstOrDefaultAsync(c => c.ChatId == chat.ChatId, token);
            if (entity == null) return;
            if (!string.Equals(entity.Type, "group", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Переименовывать можно только групповые чаты");

            var actor = await _context.ChatParticipants.AsNoTracking().FirstOrDefaultAsync(p => p.ChatId == chat.ChatId && p.UserId == actorId, token);
            if (actor == null || !IsAdministratorRole(actor.Role))
                throw new UnauthorizedAccessException("Недостаточно прав для изменения чата");

            entity.Name = chat.Name;
            await _context.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            await InvalidateChatsCacheForParticipantsBestEffortAsync(chat.ChatId, token);
        }

        private async Task InvalidateChatsCacheForParticipantsBestEffortAsync(Guid chatId, CancellationToken token)
        {
            try { await InvalidateChatsCacheForParticipantsAsync(chatId, token); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Операция с чатом завершена, но кеш участников не удалось инвалидировать chatId={ChatId}", chatId);
            }
        }

        private async Task InvalidateUserChatsCacheBestEffortAsync(Guid userId, CancellationToken token)
        {
            try { await _cache.RemoveAsync(UserChatsCacheKey(userId), token); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Операция с участником завершена, но кеш пользователя не удалось инвалидировать userId={UserId}", userId);
            }
        }

        private static string NormalizeRole(string? role) =>
            string.IsNullOrWhiteSpace(role) ? "участник" : role.Trim().ToLowerInvariant();

        private static bool IsOwnerRole(string? role) => NormalizeRole(role) is "владелец" or "owner";

        private static bool IsAdministratorRole(string? role) =>
            IsOwnerRole(role) || NormalizeRole(role) is "admin" or "админ" or "administrator" or "модератор" or "moderator";

        private static bool IsRegularParticipantRole(string? role) =>
            NormalizeRole(role) is "участник" or "member";

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
