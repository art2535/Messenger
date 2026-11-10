using Messenger.Core.DTOs.ChatFolders;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Services
{
    public class ChatFolderService : IChatFolderService
    {
        private readonly GuapMessengerContext _db;

        public ChatFolderService(GuapMessengerContext db) => _db = db;

        public async Task<List<ChatFolderDto>> GetFoldersAsync(Guid userId, CancellationToken ct = default)
        {
            var folders = await _db.ChatFolders
                .AsNoTracking()
                .Where(f => f.UserId == userId)
                .OrderBy(f => f.SortOrder)
                .ThenBy(f => f.CreatedAt)
                .Include(f => f.Chats)
                .ToListAsync(ct);

            return folders.Select(Map).ToList();
        }

        public async Task<ChatFolderDto?> GetFolderAsync(Guid userId, Guid folderId, CancellationToken ct = default)
        {
            var folder = await _db.ChatFolders
                .AsNoTracking()
                .Include(f => f.Chats)
                .FirstOrDefaultAsync(f => f.FolderId == folderId && f.UserId == userId, ct);
            return folder == null ? null : Map(folder);
        }

        public async Task<ChatFolderDto> CreateFolderAsync(Guid userId, CreateChatFolderRequest request, CancellationToken ct = default)
        {
            var name = (request.Name ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Название папки обязательно");
            if (name.Length > 64)
                name = name[..64];

            var maxOrder = await _db.ChatFolders
                .Where(f => f.UserId == userId)
                .Select(f => (int?)f.SortOrder)
                .MaxAsync(ct) ?? -1;

            var folder = new ChatFolder
            {
                FolderId = Guid.NewGuid(),
                UserId = userId,
                Name = name,
                SortOrder = maxOrder + 1,
                CreatedAt = UtcDateTime.Now
            };
            _db.ChatFolders.Add(folder);
            await _db.SaveChangesAsync(ct);

            if (request.ChatIds != null && request.ChatIds.Count > 0)
            {
                var unique = request.ChatIds.Distinct().ToList();
                var allowed = await _db.ChatParticipants
                    .Where(p => p.UserId == userId && unique.Contains(p.ChatId))
                    .Select(p => p.ChatId)
                    .ToListAsync(ct);

                foreach (var chatId in allowed)
                {
                    _db.ChatFolderChats.Add(new ChatFolderChat
                    {
                        Id = Guid.NewGuid(),
                        FolderId = folder.FolderId,
                        ChatId = chatId
                    });
                }
                await _db.SaveChangesAsync(ct);
            }

            return (await GetFolderAsync(userId, folder.FolderId, ct))!;
        }

        public async Task<ChatFolderDto?> UpdateFolderAsync(Guid userId, Guid folderId, UpdateChatFolderRequest request, CancellationToken ct = default)
        {
            var folder = await _db.ChatFolders
                .FirstOrDefaultAsync(f => f.FolderId == folderId && f.UserId == userId, ct);
            if (folder == null) return null;

            if (request.Name != null)
            {
                var name = request.Name.Trim();
                if (string.IsNullOrEmpty(name))
                    throw new ArgumentException("Название папки не может быть пустым");
                if (name.Length > 64) name = name[..64];
                folder.Name = name;
            }

            if (request.SortOrder.HasValue)
                folder.SortOrder = request.SortOrder.Value;

            await _db.SaveChangesAsync(ct);

            // Состав папки обновляем отдельно через ExecuteDelete — без concurrency на tracked collection
            if (request.ChatIds != null)
            {
                var unique = request.ChatIds.Distinct().ToList();
                var allowed = await _db.ChatParticipants
                    .Where(p => p.UserId == userId && unique.Contains(p.ChatId))
                    .Select(p => p.ChatId)
                    .ToListAsync(ct);

                await _db.ChatFolderChats
                    .Where(c => c.FolderId == folderId)
                    .ExecuteDeleteAsync(ct);

                if (allowed.Count > 0)
                {
                    foreach (var chatId in allowed)
                    {
                        _db.ChatFolderChats.Add(new ChatFolderChat
                        {
                            Id = Guid.NewGuid(),
                            FolderId = folderId,
                            ChatId = chatId
                        });
                    }
                    await _db.SaveChangesAsync(ct);
                }
            }

            return await GetFolderAsync(userId, folderId, ct);
        }

        public async Task<bool> DeleteFolderAsync(Guid userId, Guid folderId, CancellationToken ct = default)
        {
            var exists = await _db.ChatFolders
                .AnyAsync(f => f.FolderId == folderId && f.UserId == userId, ct);
            if (!exists) return false;

            await _db.ChatFolderChats
                .Where(c => c.FolderId == folderId)
                .ExecuteDeleteAsync(ct);

            await _db.ChatFolders
                .Where(f => f.FolderId == folderId && f.UserId == userId)
                .ExecuteDeleteAsync(ct);

            return true;
        }

        public async Task<bool> AddChatAsync(Guid userId, Guid folderId, Guid chatId, CancellationToken ct = default)
        {
            var folder = await _db.ChatFolders
                .FirstOrDefaultAsync(f => f.FolderId == folderId && f.UserId == userId, ct);
            if (folder == null) return false;

            var isMember = await _db.ChatParticipants
                .AnyAsync(p => p.UserId == userId && p.ChatId == chatId, ct);
            if (!isMember) return false;

            var exists = await _db.ChatFolderChats
                .AnyAsync(c => c.FolderId == folderId && c.ChatId == chatId, ct);
            if (exists) return true;

            _db.ChatFolderChats.Add(new ChatFolderChat
            {
                Id = Guid.NewGuid(),
                FolderId = folderId,
                ChatId = chatId
            });
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> RemoveChatAsync(Guid userId, Guid folderId, Guid chatId, CancellationToken ct = default)
        {
            var folderOk = await _db.ChatFolders
                .AnyAsync(f => f.FolderId == folderId && f.UserId == userId, ct);
            if (!folderOk) return false;

            await _db.ChatFolderChats
                .Where(c => c.FolderId == folderId && c.ChatId == chatId)
                .ExecuteDeleteAsync(ct);
            return true;
        }

        private static ChatFolderDto Map(ChatFolder f) => new()
        {
            FolderId = f.FolderId,
            Name = f.Name,
            SortOrder = f.SortOrder,
            ChatIds = f.Chats?.Select(c => c.ChatId).ToList() ?? new List<Guid>()
        };
    }
}
