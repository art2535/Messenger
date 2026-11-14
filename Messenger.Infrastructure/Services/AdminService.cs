using Messenger.Core.DTOs.Admin;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Services
{
    public class AdminService : IAdminService
    {
        private readonly GuapMessengerContext _context;
        private readonly ILogger<AdminService>? _logger;
        private static readonly Guid AdminRoleId = Guid.Parse("e5f6a7b8-c9d0-1234-efab-345678901234");

        public AdminService(GuapMessengerContext context, ILogger<AdminService>? logger = null)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<bool> IsSystemAdminAsync(Guid userId, CancellationToken ct = default)
        {
            return await _context.Users.AsNoTracking()
                .Where(u => u.UserId == userId)
                .SelectMany(u => u.Roles)
                .AnyAsync(r => r.RoleId == AdminRoleId || r.Name == "Администратор", ct);
        }

        public async Task<AdminActivityStatsDto> GetActivityStatsAsync(CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var d1 = now.AddDays(-1);
            var d7 = now.AddDays(-7);
            var onlineThreshold = now.AddMinutes(-15);

            var totalUsers = await _context.Users.AsNoTracking().CountAsync(ct);

            var onlineUsers = await _context.UserStatuses.AsNoTracking()
                .CountAsync(s => s.Online || (s.LastActivity != null && s.LastActivity >= onlineThreshold), ct);

            var msg24 = await _context.Messages.AsNoTracking().CountAsync(m => m.SendTime >= d1, ct);
            var msg7 = await _context.Messages.AsNoTracking().CountAsync(m => m.SendTime >= d7, ct);
            var activeChats = await _context.Messages.AsNoTracking()
                .Where(m => m.SendTime >= d1)
                .Select(m => m.ChatId)
                .Distinct()
                .CountAsync(ct);

            int pendingReports = 0;
            int activeMutes = 0;
            try
            {
                pendingReports = await _context.Set<MessageReport>().AsNoTracking()
                    .CountAsync(r => r.Status == "pending", ct);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "MessageReports n/a");
            }

            try
            {
                activeMutes = await _context.Set<UserMute>().AsNoTracking()
                    .CountAsync(m => m.IsActive && m.MutedUntil > now, ct);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "UserMutes n/a");
            }

            var sendTimes = await _context.Messages.AsNoTracking()
                .Where(m => m.SendTime >= d7)
                .Select(m => m.SendTime)
                .ToListAsync(ct);

            var perDay = sendTimes
                .Select(t =>
                {
                    var utc = t.Kind switch
                    {
                        DateTimeKind.Utc => t,
                        DateTimeKind.Local => t.ToUniversalTime(),
                        _ => DateTime.SpecifyKind(t, DateTimeKind.Utc)
                    };
                    return DateOnly.FromDateTime(utc);
                })
                .GroupBy(d => d)
                .Select(g => new DailyCountDto { Date = g.Key, Count = g.Count() })
                .OrderBy(x => x.Date)
                .ToList();

            var topRaw = await _context.Messages.AsNoTracking()
                .Where(m => m.SendTime >= d7)
                .GroupBy(m => m.ChatId)
                .Select(g => new { ChatId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync(ct);

            var chatIds = topRaw.Select(t => t.ChatId).ToList();
            var names = chatIds.Count == 0
                ? new Dictionary<Guid, string?>()
                : await _context.Chats.AsNoTracking()
                    .Where(c => chatIds.Contains(c.ChatId))
                    .Select(c => new { c.ChatId, c.Name })
                    .ToDictionaryAsync(x => x.ChatId, x => (string?)x.Name, ct);

            return new AdminActivityStatsDto
            {
                TotalUsers = totalUsers,
                OnlineUsers = onlineUsers,
                MessagesLast24h = msg24,
                MessagesLast7d = msg7,
                ActiveChatsLast24h = activeChats,
                PendingReports = pendingReports,
                ActiveMutes = activeMutes,
                MessagesPerDay = perDay,
                TopChats = topRaw.Select(t => new TopChatDto
                {
                    ChatId = t.ChatId,
                    ChatName = names.GetValueOrDefault(t.ChatId),
                    MessageCount = t.Count
                }).ToList()
            };
        }
    }
}
