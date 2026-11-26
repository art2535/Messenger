using Messenger.Core.DTOs.Broadcasts;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Services
{
    public class BroadcastService : IBroadcastService
    {
        private readonly GuapMessengerContext _context;

        public BroadcastService(GuapMessengerContext context)
        {
            _context = context;
        }

        public async Task<BroadcastCreatedResponse> CreateBroadcastAsync(CreateBroadcastRequest request, Guid senderId)
        {
            var existingIds = await _context.Users
                .Where(u => request.RecipientIds.Contains(u.UserId))
                .Select(u => u.UserId)
                .ToListAsync();

            if (existingIds.Count != request.RecipientIds.Count)
                throw new ArgumentException("Один или несколько получателей не существуют");

            var broadcast = new Broadcast
            {
                Title = request.Title,
                MessageText = request.MessageText,
                SenderId = senderId,
                CreatedAt = DateTime.UtcNow,
                TotalRecipients = existingIds.Count
            };

            _context.Broadcasts.Add(broadcast);
            await _context.SaveChangesAsync();

            var recipients = existingIds.Select(uid => new BroadcastRecipient
            {
                BroadcastId = broadcast.BroadcastId,
                UserId = uid,
                SentAt = DateTime.UtcNow
            }).ToList();

            _context.BroadcastRecipients.AddRange(recipients);
            await _context.SaveChangesAsync();

            return new BroadcastCreatedResponse
            {
                BroadcastId = broadcast.BroadcastId,
                TotalRecipients = broadcast.TotalRecipients,
                CreatedAt = broadcast.CreatedAt
            };
        }

        public async Task<BroadcastSummaryDto?> GetBroadcastSummaryAsync(Guid id, Guid currentUserId, bool isAdmin)
        {
            var broadcast = await _context.Broadcasts
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.BroadcastId == id);
            if (broadcast == null)
                return null;

            if (broadcast.SenderId != currentUserId && !isAdmin)
                throw new UnauthorizedAccessException("Нет прав на просмотр этой рассылки");

            var recipients = await _context.BroadcastRecipients
                .AsNoTracking()
                .Where(r => r.BroadcastId == id)
                .ToListAsync();
            var statsData = await _context.BroadcastRecipients
                .Where(r => r.BroadcastId == id)
                .GroupBy(_ => 1)
                .Select(g => new { ReadCount = g.Count(r => r.IsRead), FirstRead = g.Min(r => r.ReadAt) })
                .FirstOrDefaultAsync();

            return new BroadcastSummaryDto
            {
                BroadcastId = broadcast.BroadcastId,
                Title = broadcast.Title,
                MessageText = broadcast.MessageText,
                SenderId = broadcast.SenderId,
                CreatedAt = broadcast.CreatedAt,
                TotalRecipients = broadcast.TotalRecipients,
                ReadCount = statsData?.ReadCount ?? 0,
                Recipients = recipients.Select(r => new RecipientStatusDto
                {
                    UserId = r.UserId,
                    IsRead = r.IsRead,
                    ReadAt = r.ReadAt
                }).ToList()
            };
        }

        public async Task<MarkAsReadResponse> MarkAsReadAsync(Guid broadcastId, Guid userId)
        {
            var recipient = await _context.BroadcastRecipients
                .FirstOrDefaultAsync(r => r.BroadcastId == broadcastId && r.UserId == userId);
            if (recipient == null)
                throw new KeyNotFoundException("Вы не являетесь получателем этой рассылки");

            if (recipient.IsRead)
            {
                return new MarkAsReadResponse
                {
                    Success = true,
                    ReadAt = recipient.ReadAt
                };
            }

            recipient.IsRead = true;
            recipient.ReadAt = DateTime.UtcNow;

            _context.BroadcastRecipients.Update(recipient);
            await _context.SaveChangesAsync();

            return new MarkAsReadResponse
            {
                Success = true,
                ReadAt = recipient.ReadAt
            };
        }

        public async Task<List<object>> GetMyBroadcastsAsync(Guid userId, bool unreadOnly = true)
        {
            var query = _context.BroadcastRecipients
                .AsNoTracking()
                .Where(r => r.UserId == userId)
                .Include(r => r.Broadcast)
                .AsQueryable();
            if (unreadOnly)
                query = query.Where(r => !r.IsRead);
            var items = (await query.OrderByDescending(r => r.Broadcast.CreatedAt).ToListAsync())
                .Select(r => (r.Broadcast, r.IsRead, r.ReadAt)).ToList();

            return items.Select(x => new
            {
                x.Broadcast.BroadcastId,
                x.Broadcast.Title,
                x.Broadcast.MessageText,
                x.Broadcast.CreatedAt,
                IsRead = x.IsRead,
                ReadAt = x.ReadAt
            }).ToList<object>();
        }
    }
}
