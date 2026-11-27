using Messenger.Application.Common;
using Messenger.Application.Interfaces;
using Messenger.Application.DTOs.Notifications;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Services
{
    public class NotificationService : INotificationService
    {
        private readonly GuapMessengerContext _context;
        private readonly IEncryptionService _encryptionService;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(GuapMessengerContext context, IEncryptionService encryptionService,
            ILogger<NotificationService> logger)
        {
            _context = context;
            _encryptionService = encryptionService;
            _logger = logger;
        }

        public Task<Guid> CreateNotificationAsync(Guid userId, string text, CancellationToken token = default)
            => CreateNotificationCoreAsync(userId, text, null, token);

        public Task<Guid> CreateNotificationAsync(
            Guid userId,
            string text,
            string deduplicationKey,
            CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(deduplicationKey))
                throw new ArgumentException("Ключ идемпотентности не может быть пустым.", nameof(deduplicationKey));

            if (deduplicationKey.Length > 256)
                throw new ArgumentOutOfRangeException(nameof(deduplicationKey), "Ключ идемпотентности не может превышать 256 символов.");

            return CreateNotificationCoreAsync(userId, text, deduplicationKey, token);
        }

        private async Task<Guid> CreateNotificationCoreAsync(
            Guid userId,
            string text,
            string? deduplicationKey,
            CancellationToken token)
        {
            if (deduplicationKey != null)
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(token);
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({deduplicationKey}, 0))",
                    token);

                var existingId = await _context.Notifications
                    .AsNoTracking()
                    .Where(n => n.DeduplicationKey == deduplicationKey)
                    .Select(n => (Guid?)n.NotificationId)
                    .FirstOrDefaultAsync(token);

                if (existingId.HasValue)
                {
                    var existingOwnerId = await _context.Notifications.AsNoTracking()
                        .Where(n => n.NotificationId == existingId.Value)
                        .Select(n => n.UserId)
                        .FirstAsync(token);
                    if (existingOwnerId != userId)
                        throw new InvalidOperationException("Ключ идемпотентности уже используется другим пользователем");

                    await transaction.CommitAsync(token);
                    return existingId.Value;
                }

                var idempotentNotification = new Notification
                {
                    NotificationId = Guid.NewGuid(),
                    UserId = userId,
                    Text = _encryptionService.Encrypt(text),
                    DeduplicationKey = deduplicationKey,
                    CreationDate = UtcDateTime.Now,
                    Read = false
                };

                await _context.Notifications.AddAsync(idempotentNotification, token);
                await _context.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
                return idempotentNotification.NotificationId;
            }

            var notification = new Notification
            {
                NotificationId = Guid.NewGuid(),
                UserId = userId,
                Text = _encryptionService.Encrypt(text),
                CreationDate = UtcDateTime.Now,
                Read = false
            };

            await _context.Notifications.AddAsync(notification, token);
            await _context.SaveChangesAsync(token);
            return notification.NotificationId;
        }

        public async Task<IEnumerable<NotificationDto>> GetNotificationsAsync(Guid userId, CancellationToken token = default)
        {
            var notifications = await _context.Notifications
                .AsNoTracking()
                .Where(notification => notification.UserId == userId)
                .OrderByDescending(n => n.CreationDate)
                .ToListAsync(token);

            return notifications.Select(notification => new NotificationDto
            {
                NotificationId = notification.NotificationId,
                UserId = notification.UserId,
                Text = _encryptionService.TryDecryptSafe(notification.Text),
                CreationDate = notification.CreationDate,
                Read = notification.Read,
                ReadAt = notification.ReadAt
            }).ToList();
        }

        public async Task MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken token = default)
        {
            var updatedRows = await _context.Notifications
                .Where(n => n.NotificationId == notificationId && n.UserId == userId && !n.Read)
                .ExecuteUpdateAsync(property => property
                    .SetProperty(n => n.Read, true)
                    .SetProperty(n => n.ReadAt, UtcDateTime.Now),
                token);

            if (updatedRows == 0)
            {
                _logger.LogWarning("Нет обновленных строк в таблице уведомлений");
            }
        }

        public async Task<NotificationDto?> GetNotificationAsync(Guid userId, Guid notificationId, CancellationToken token = default)
        {
            var notification = await _context.Notifications
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.NotificationId == notificationId && n.UserId == userId, token);
            if (notification == null)
                return null;

            return new NotificationDto
            {
                NotificationId = notification.NotificationId,
                UserId = notification.UserId,
                Text = _encryptionService.TryDecryptSafe(notification.Text),
                CreationDate = notification.CreationDate,
                Read = notification.Read,
                ReadAt = notification.ReadAt
            };
        }
    }
}
