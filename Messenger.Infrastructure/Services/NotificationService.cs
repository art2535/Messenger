using Messenger.Core.Interfaces;
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

        public async Task<Guid> CreateNotificationAsync(Guid userId, string text, CancellationToken token = default)
        {
            string encryptedText = _encryptionService.Encrypt(text);

            var notification = new Notification
            {
                NotificationId = Guid.NewGuid(),
                UserId = userId,
                Text = encryptedText,
                CreationDate = Notification.Now,
                Read = false
            };

            await _context.Notifications.AddAsync(notification, token);
            await _context.SaveChangesAsync(token);
            return notification.NotificationId;
        }

        public async Task<IEnumerable<Notification>> GetNotificationsAsync(Guid userId, CancellationToken token = default)
        {
            var notifications = await _context.Notifications
                .Where(notification => notification.UserId == userId)
                .OrderByDescending(n => n.CreationDate)
                .ToListAsync(token);

            foreach (var notification in notifications)
            {
                notification.Text = _encryptionService.TryDecryptSafe(notification.Text);
            }

            return notifications;
        }

        public async Task MarkAsReadAsync(Guid notificationId, CancellationToken token = default)
        {
            var updatedRows = await _context.Notifications
                .Where(n => n.NotificationId == notificationId)
                .ExecuteUpdateAsync(property => property
                    .SetProperty(n => n.Read, true)
                    .SetProperty(n => n.ReadAt, Notification.Now),
                token);

            if (updatedRows == 0)
            {
                _logger.LogWarning("Нет обновленных строк в таблице уведомлений");
            }
        }

        public async Task<Notification?> GetNotificationAsync(Guid notificationId, CancellationToken token = default)
        {
            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.NotificationId == notificationId, token);
            if (notification != null)
            {
                notification.Text = _encryptionService.TryDecryptSafe(notification.Text);
            }
            return notification;
        }
    }
}
