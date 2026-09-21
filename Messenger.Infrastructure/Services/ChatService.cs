using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Services
{
    public class ChatService : IChatService
    {
        private readonly GuapMessengerContext _context;
        private readonly IUserService _userService;
        private readonly IEncryptionService _encryptionService;

        public ChatService(GuapMessengerContext context, IUserService userService, IEncryptionService encryptionService)
        {
            _context = context;
            _userService = userService;
            _encryptionService = encryptionService;
        }

        public async Task<Chat> CreateChatAsync(string name, string type, Guid creatorId, CancellationToken token = default)
        {
            var chat = new Chat
            {
                ChatId = Guid.NewGuid(),
                Name = name,
                Type = type,
                UserId = creatorId,
                CreationDate = DateTime.Now
            };

            var participant = new ChatParticipant
            {
                ChatId = chat.ChatId,
                UserId = creatorId,
                Role = "владелец",
                JoinDate = DateTime.Now
            };

            await _context.Chats.AddAsync(chat, token);
            await _context.SaveChangesAsync(token);
            await _context.ChatParticipants.AddAsync(participant, token);
            await _context.SaveChangesAsync(token);

            return chat;
        }

        public async Task<List<object>> GetUserChatsWithLastMessageAsync(Guid userId, CancellationToken token = default)
        {
            var chats = await _context.Chats
                .Include(c => c.ChatParticipants)
                    .ThenInclude(cp => cp.User)
                        .ThenInclude(u => u.Account)
                .Include(c => c.Messages)
                .Where(c => c.ChatParticipants.Any(cp => cp.UserId == userId))
                .ToListAsync(token);

            var result = new List<object>();
            foreach (var chat in chats)
            {
                var lastMsg = chat.Messages?.OrderByDescending(m => m.SendTime).FirstOrDefault();

                bool isBlocked = false;
                if (chat.Type == "private")
                {
                    var otherParticipant = chat.ChatParticipants.FirstOrDefault(p => p.UserId != userId);
                    if (otherParticipant != null)
                    {
                        bool blockedByMe = await _userService.IsBlockedByAsync(userId, otherParticipant.UserId, token);
                        bool blockedByThem = await _userService.IsBlockedByAsync(otherParticipant.UserId, userId, token);
                        isBlocked = blockedByMe || blockedByThem;
                    }
                }

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
                else if (chat.Messages?.Any() == true)
                {
                    decryptedLastMessage = "Вложение";
                }

                int unreadCount = chat.Messages?.Count(m => m.SenderId != userId && m.ReadTime == null) ?? 0;

                result.Add(new
                {
                    chatId = chat.ChatId,
                    name = chat.Type == "private"
                        ? chat.ChatParticipants
                            .Where(p => p.UserId != userId)
                            .Select(p => $"{p.User.FirstName} {p.User.LastName}".Trim())
                            .FirstOrDefault() ?? "Приватный чат"
                        : chat.Name,
                    avatar = chat.Type == "private"
                        ? chat.ChatParticipants
                            .Where(p => p.UserId != userId)
                            .Select(p => p.User.Account?.Avatar)
                            .FirstOrDefault()
                        : null,
                    type = chat.Type,
                    lastMessage = decryptedLastMessage,
                    isOnline = true,
                    isBlocked = isBlocked,
                    unreadCount = unreadCount
                });
            }

            return result;
        }

        public async Task<IEnumerable<Chat>> GetUserChatsAsync(Guid userId, CancellationToken token = default)
        {
            return await _context.Chats
                .Include(c => c.ChatParticipants)
                    .ThenInclude(cp => cp.User)
                        .ThenInclude(u => u.Account)
                .Include(c => c.Messages)
                .Where(c => c.ChatParticipants.Any(cp => cp.UserId == userId))
                .ToListAsync(token);
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
                JoinDate = DateTime.Now
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
        }

        public async Task DeleteParticipantFromChatAsync(Guid chatId, Guid userId, CancellationToken token = default)
        {
            var deleteParticipant = await _context.ChatParticipants.FirstOrDefaultAsync(part => part.ChatId == chatId && part.UserId == userId, token);

            if (deleteParticipant != null)
            {
                _context.ChatParticipants.Remove(deleteParticipant);
                await _context.SaveChangesAsync(token);
            }
        }

        public async Task DeleteChatAsync(Chat chat, CancellationToken token = default)
        {
            _context.Chats.Remove(chat);
            await _context.SaveChangesAsync(token);
        }

        public async Task<Chat?> GetChatByIdAsync(Guid chatId, CancellationToken token = default)
        {
            return await _context.Chats
                .Include(c => c.ChatParticipants)
                .FirstOrDefaultAsync(c => c.ChatId == chatId, token);
        }

        public async Task<IEnumerable<ChatParticipant>> GetChatParticipantsAsync(Guid chatId, 
            CancellationToken token = default)
        {
            return await _context.ChatParticipants
                .Where(part => part.ChatId == chatId)
                .Include(p => p.User)
                    .ThenInclude(u => u.Account)
                .ToListAsync(token);
        }

        public async Task UpdateChatAsync(Chat chat, CancellationToken token = default)
        {
            _context.Chats.Update(chat);
            await _context.SaveChangesAsync(token);
        }
    }
}
