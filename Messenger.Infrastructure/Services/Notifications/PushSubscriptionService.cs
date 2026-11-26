using Messenger.Core.DTOs.Push;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using WebPush;
using PushSubscription = Messenger.Core.Models.PushSubscription;

namespace Messenger.Infrastructure.Services.Notifications
{
    public class PushSubscriptionService : IPushSubscriptionService
    {
        private readonly GuapMessengerContext _context;
        private readonly IChatService _chatService;
        private readonly WebPushClient _webPushClient;
        private readonly VapidDetails _vapidDetails;
        private readonly ILogger<PushSubscriptionService> _logger;
        private readonly IEncryptionService _encryptionService;
        private readonly IUserService _userService;
        private readonly INotificationService _notificationService;

        public PushSubscriptionService(GuapMessengerContext context, IChatService chatService,
            WebPushClient webPushClient, IConfiguration configuration, INotificationService notificationService,
            ILogger<PushSubscriptionService> logger, IEncryptionService encryptionService, IUserService userService)
        {
            _context = context;
            _chatService = chatService;
            _webPushClient = webPushClient;
            _logger = logger;

            var vapidSection = configuration.GetSection("Vapid");
            _vapidDetails = new VapidDetails(vapidSection["Subject"], vapidSection["PublicKey"]!,
                vapidSection["PrivateKey"]!);
            _encryptionService = encryptionService;
            _userService = userService;
            _notificationService = notificationService;
        }

        public async Task<List<PushSubscription>> GetSubscriptionsByUserIdAsync(Guid userId, CancellationToken ct = default)
        {
            return await _context.PushSubscriptions
                .Where(s => s.UserId == userId)
                .ToListAsync(ct);
        }

        public async Task AddSubscriptionAsync(PushSubscription subscription, CancellationToken ct = default)
        {
            _context.PushSubscriptions.Add(subscription);
            await _context.SaveChangesAsync(ct);
        }

        public async Task EnsurePushEnabledAsync(Guid userId, CancellationToken token = default)
        {
            var settings = await _context.AccountSettings
                .FirstOrDefaultAsync(s => s.AccountId == userId, token);

            if (settings == null)
            {
                _context.AccountSettings.Add(new AccountSetting
                {
                    AccountId = userId,
                    PushEnabled = true,
                    NotifyMessages = true,
                    NotifyGroupChats = true,
                    NotifyMentions = true
                });
                await _context.SaveChangesAsync(token);
                return;
            }

            if (!settings.PushEnabled)
            {
                settings.PushEnabled = true;
                await _context.SaveChangesAsync(token);
            }
        }

        public async Task RemoveSubscriptionAsync(Guid id, CancellationToken ct = default)
        {
            var sub = await _context.PushSubscriptions.FindAsync(new object[] { id }, ct);
            if (sub != null)
            {
                _context.PushSubscriptions.Remove(sub);
                await _context.SaveChangesAsync(ct);
            }
        }

        public async Task UpdateSubscriptionAsync(PushSubscription subscription, CancellationToken ct = default)
        {
            _context.PushSubscriptions.Update(subscription);
            await _context.SaveChangesAsync(ct);
        }

        public async Task RemoveByEndpointAsync(string endpoint, CancellationToken ct = default)
        {
            var subscriptions = await _context.PushSubscriptions
                .Where(s => s.Endpoint == endpoint)
                .ToListAsync(ct);
            if (subscriptions.Any())
            {
                _context.PushSubscriptions.RemoveRange(subscriptions);
                await _context.SaveChangesAsync(ct);
            }
        }

        public async Task<AccountSetting?> GetPushSettingsAsync(Guid userId, CancellationToken token = default)
        {
            return await _context.AccountSettings
                .FirstOrDefaultAsync(s => s.AccountId == userId, token);
        }

        public async Task SavePushSettingsAsync(Guid userId, PushSubscriptionUpdateRequest request, CancellationToken token = default)
        {
            var settings = await _context.AccountSettings
                .FirstOrDefaultAsync(s => s.AccountId == userId, token);
            if (settings == null)
            {
                settings = new AccountSetting
                {
                    AccountId = userId,
                    PushEnabled = request.PushEnabled,
                    NotifyMessages = request.NotifyMessages,
                    NotifyGroupChats = request.NotifyGroupChats,
                    NotifyMentions = request.NotifyMentions
                };
                _context.AccountSettings.Add(settings);
            }
            else
            {
                settings.PushEnabled = request.PushEnabled;
                settings.NotifyMessages = request.NotifyMessages;
                settings.NotifyGroupChats = request.NotifyGroupChats;
                settings.NotifyMentions = request.NotifyMentions;
            }
            await _context.SaveChangesAsync(token);

            if (!request.PushEnabled)
            {
                var allSubs = await _context.PushSubscriptions
                    .Where(s => s.UserId == userId)
                    .ToListAsync(token);
                if (allSubs.Any())
                {
                    _context.PushSubscriptions.RemoveRange(allSubs);
                    await _context.SaveChangesAsync(token);
                }
                _logger.LogInformation("Все push-подписки удалены для пользователя {UserId}", userId);
            }
        }

        public async Task SendPushToOfflineUsersAsync(Guid chatId, Guid senderId, string senderName,
            string? messageText, bool hasAttachments, bool isMention = false, CancellationToken cancellationToken = default)
        {
            var deliveryErrors = new List<Exception>();

            try
            {
                string body = "Новое сообщение";

                if (!string.IsNullOrWhiteSpace(messageText))
                {
                    string decrypted = _encryptionService.TryDecryptSafe(messageText);

                    if (!string.IsNullOrEmpty(decrypted) && !decrypted.StartsWith("Encrypted:"))
                    {
                        body = decrypted.Length > 60 ? decrypted[..57] + "..." : decrypted;
                    }
                    else
                    {
                        body = messageText.Length > 60 ? messageText[..57] + "..." : messageText;
                    }
                }
                else if (hasAttachments)
                {
                    body = "Прикрепил файл";
                }

                var chat = await _chatService.GetChatByIdAsync(chatId, cancellationToken);
                if (chat == null) 
                    return;

                bool isGroupChat = chat.Type?.ToLower() == "group";
                var participants = await _chatService.GetChatParticipantsAsync(chatId, cancellationToken);

                foreach (var participant in participants.Where(p => p.UserId != senderId))
                {
                    var settings = await GetPushSettingsAsync(participant.UserId, cancellationToken);

                    if (settings != null && !settings.PushEnabled)
                    {
                        _logger.LogWarning("Пропуск пользователя {UserId}: Push-уведомления полностью отключены", participant.UserId);
                        continue;
                    }

                    bool notifyGroup = settings?.NotifyGroupChats ?? true;
                    bool notifyMessages = settings?.NotifyMessages ?? true;
                    bool notifyMentions = settings?.NotifyMentions ?? true;
                    bool notifyPreview = settings?.NotifyPreview ?? true;

                    if (isGroupChat && !notifyGroup)
                    {
                        _logger.LogWarning("Пропуск пользователя {UserId}: Групповые уведомления отключены", participant.UserId);
                        continue;
                    }

                    if (!isGroupChat && !notifyMessages)
                    {
                        _logger.LogWarning("Пропуск пользователя {UserId}: Уведомления о сообщениях отключены", participant.UserId);
                        continue;
                    }

                    if (isMention && !notifyMentions)
                    {
                        _logger.LogWarning("Пропуск пользователя {UserId}: Уведомления об упоминаниях отключены", participant.UserId);
                        continue;
                    }

                    try
                    {
                        var groupName = !string.IsNullOrWhiteSpace(chat.Name) ? chat.Name : "Групповой чат";
                        var pushTitle = isGroupChat
                            ? $"Новое сообщение в группе \"{groupName}\""
                            : $"Новое сообщение от {senderName}";

                        string previewBody;
                        if (notifyPreview)
                        {
                            previewBody = isGroupChat
                                ? $"{senderName}: {body}"
                                : body;
                        }
                        else
                        {
                            previewBody = isGroupChat
                                ? $"{senderName}: новое сообщение"
                                : "Новое сообщение";
                        }

                        string notificationText = notifyPreview
                            ? (isGroupChat
                                ? $"{pushTitle}: {senderName}: {body}"
                                : $"{pushTitle}: {body}")
                            : pushTitle;

                        var notificationId = await _notificationService.CreateNotificationAsync(participant.UserId,
                            _encryptionService.Encrypt(notificationText), cancellationToken);

                        var subscriptions = await _context.PushSubscriptions
                            .Where(s => s.UserId == participant.UserId)
                            .ToListAsync(cancellationToken);

                        if (!subscriptions.Any())
                        {
                            _logger.LogInformation("У пользователя {UserId} нет активных push-подписок", participant.UserId);
                            continue;
                        }

                        foreach (var sub in subscriptions)
                        {
                            if (string.IsNullOrEmpty(sub.P256dh) || string.IsNullOrEmpty(sub.Auth))
                                continue;

                            try
                            {
                                var pushSubscription = new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);

                                var payload = new
                                {
                                    title = pushTitle,
                                    body = previewBody,
                                    sender = senderName,
                                    chatId = chatId.ToString(),
                                    chatType = isGroupChat ? "group" : "private",
                                    chatName = isGroupChat ? groupName : senderName,
                                    notificationId = notificationId.ToString(),
                                    silent = !(settings?.NotifySound ?? true),
                                    preview = notifyPreview
                                };

                                await _webPushClient.SendNotificationAsync(pushSubscription, JsonSerializer.Serialize(payload),
                                    _vapidDetails, cancellationToken);

                                var subEntity = await _context.PushSubscriptions.FindAsync(new object[] { sub.Id }, cancellationToken);
                                if (subEntity != null)
                                {
                                    subEntity.LastUsedAt = DateTime.UtcNow;
                                    await _context.SaveChangesAsync(cancellationToken);
                                }
                            }
                            catch (WebPushException webEx) when (webEx.Message.Contains("no longer valid") ||
                                                                 webEx.Message.Contains("unsubscribed") ||
                                                                 webEx.Message.Contains("expired"))
                            {
                                _logger.LogWarning("Подписка пользователя {UserId} устарела. Удаляем...", participant.UserId);
                                var toRemove = await _context.PushSubscriptions
                                    .Where(s => s.Endpoint == sub.Endpoint)
                                    .ToListAsync(cancellationToken);
                                if (toRemove.Any())
                                {
                                    _context.PushSubscriptions.RemoveRange(toRemove);
                                    await _context.SaveChangesAsync(cancellationToken);
                                }
                            }
                            catch (Exception ex)
                            {
                                deliveryErrors.Add(ex);
                                _logger.LogWarning(ex, "Ошибка отправки push пользователю {UserId}", participant.UserId);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        deliveryErrors.Add(ex);
                        _logger.LogWarning(ex,
                            "Ошибка при создании уведомления или отправке push для пользователя {UserId}",
                            participant.UserId);
                    }
                }

                if (deliveryErrors.Count > 0)
                {
                    throw new AggregateException(
                        "Не удалось доставить push всем получателям.",
                        deliveryErrors);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Общая ошибка при отправке push для чата {ChatId}", chatId);
                throw;
            }
        }
        public async Task SendPushToOfflineUsersIdempotentlyAsync(
            Guid outboxId,
            Guid chatId,
            Guid senderId,
            string senderName,
            string? messageText,
            bool hasAttachments,
            bool isMention = false,
            CancellationToken cancellationToken = default)
        {
            var deliveryErrors = new List<Exception>();

            var chat = await _chatService.GetChatByIdAsync(chatId, cancellationToken);
            if (chat == null)
                return;

            var participants = await _chatService.GetChatParticipantsAsync(
                chatId,
                cancellationToken);
            var isGroupChat = chat.Type?.ToLower() == "group";
            var groupName = !string.IsNullOrWhiteSpace(chat.Name)
                ? chat.Name
                : "Групповой чат";
            var body = BuildPushBody(messageText, hasAttachments);

            foreach (var participant in participants.Where(p => p.UserId != senderId))
            {
                try
                {
                    var settings = await GetPushSettingsAsync(
                        participant.UserId,
                        cancellationToken);

                    if (settings != null && !settings.PushEnabled)
                        continue;

                    var notifyGroup = settings?.NotifyGroupChats ?? true;
                    var notifyMessages = settings?.NotifyMessages ?? true;
                    var notifyMentions = settings?.NotifyMentions ?? true;

                    if (isGroupChat && !notifyGroup)
                        continue;

                    if (!isGroupChat && !notifyMessages)
                        continue;

                    if (isMention && !notifyMentions)
                        continue;

                    var subscriptions = await _context.PushSubscriptions
                        .Where(s => s.UserId == participant.UserId)
                        .ToListAsync(cancellationToken);

                    if (subscriptions.Count == 0)
                        continue;

                    var notifyPreview = settings?.NotifyPreview ?? true;
                    var pushTitle = isGroupChat
                        ? $"Новое сообщение в группе \"{groupName}\""
                        : $"Новое сообщение от {senderName}";
                    var previewBody = notifyPreview
                        ? isGroupChat
                            ? $"{senderName}: {body}"
                            : body
                        : isGroupChat
                            ? $"{senderName}: новое сообщение"
                            : "Новое сообщение";
                    var notificationText = notifyPreview
                        ? isGroupChat
                            ? $"{pushTitle}: {senderName}: {body}"
                            : $"{pushTitle}: {body}"
                        : pushTitle;

                    Guid? notificationId = null;

                    foreach (var subscription in subscriptions)
                    {
                        if (string.IsNullOrWhiteSpace(subscription.P256dh) ||
                            string.IsNullOrWhiteSpace(subscription.Auth))
                            continue;

                        var key = BuildPushIdempotencyKey(
                            outboxId,
                            participant.UserId,
                            subscription.Id);
                        var delivery = await _context.MessageNotificationOutboxDeliveries
                            .SingleOrDefaultAsync(
                                item => item.IdempotencyKey == key,
                                cancellationToken);

                        if (delivery?.Status == "Delivered")
                            continue;

                        notificationId ??= await EnsureIdempotentNotificationAsync(
                            participant.UserId,
                            notificationText,
                            $"{outboxId:N}:{participant.UserId:N}:notification",
                            cancellationToken);

                        if (delivery == null)
                        {
                            delivery = new MessageNotificationOutboxDelivery
                            {
                                DeliveryId = Guid.NewGuid(),
                                OutboxId = outboxId,
                                MessageId = await GetMessageIdAsync(
                                    outboxId,
                                    cancellationToken),
                                RecipientId = participant.UserId,
                                SubscriptionId = subscription.Id,
                                Channel = "Push",
                                Status = "Processing",
                                AttemptCount = 0,
                                NextAttemptAt = DateTime.UtcNow,
                                NotificationId = notificationId,
                                IdempotencyKey = key
                            };

                            _context.MessageNotificationOutboxDeliveries.Add(delivery);
                            await _context.SaveChangesAsync(cancellationToken);
                        }
                        else
                        {
                            delivery.Status = "Processing";
                            delivery.LockedUntil = DateTime.UtcNow.AddMinutes(2);
                            delivery.NotificationId = notificationId;
                            await _context.SaveChangesAsync(cancellationToken);
                        }

                        try
                        {
                            var payload = BuildPushPayload(
                                pushTitle,
                                previewBody,
                                senderName,
                                chatId,
                                isGroupChat,
                                groupName,
                                notificationId.Value,
                                settings?.NotifySound ?? true,
                                notifyPreview);

                            var pushSubscription = new WebPush.PushSubscription(
                                subscription.Endpoint,
                                subscription.P256dh,
                                subscription.Auth);

                            await _webPushClient.SendNotificationAsync(
                                pushSubscription,
                                JsonSerializer.Serialize(payload),
                                _vapidDetails,
                                cancellationToken);

                            delivery.Status = "Delivered";
                            delivery.DeliveredAt = DateTime.UtcNow;
                            delivery.LockedUntil = null;
                            delivery.LastError = null;
                            await _context.SaveChangesAsync(cancellationToken);

                            subscription.LastUsedAt = DateTime.UtcNow;
                            await _context.SaveChangesAsync(cancellationToken);
                        }
                        catch (WebPushException webEx) when (
                            webEx.Message.Contains("no longer valid") ||
                            webEx.Message.Contains("unsubscribed") ||
                            webEx.Message.Contains("expired"))
                        {
                            _logger.LogWarning(
                                "Подписка пользователя {UserId} устарела. Удаляем...",
                                participant.UserId);

                            var toRemove = await _context.PushSubscriptions
                                .Where(s => s.Endpoint == subscription.Endpoint)
                                .ToListAsync(cancellationToken);

                            if (toRemove.Any())
                            {
                                _context.PushSubscriptions.RemoveRange(toRemove);
                                delivery.Status = "Skipped";
                                delivery.LockedUntil = null;
                                delivery.LastError = "Push-подписка устарела.";
                                await _context.SaveChangesAsync(cancellationToken);
                            }
                        }
                        catch (Exception exception)
                        {
                            delivery.AttemptCount++;
                            delivery.Status = "Pending";
                            delivery.LockedUntil = null;
                            delivery.LastError = exception.Message.Length > 2000
                                ? exception.Message[..2000]
                                : exception.Message;
                            delivery.NextAttemptAt = DateTime.UtcNow.AddSeconds(
                                Math.Min(3600, Math.Pow(2, delivery.AttemptCount)));
                            await _context.SaveChangesAsync(cancellationToken);

                            deliveryErrors.Add(exception);
                            _logger.LogWarning(
                                exception,
                                "Ошибка идемпотентной push-доставки пользователю {UserId}",
                                participant.UserId);
                        }
                    }
                }
                catch (Exception exception)
                {
                    deliveryErrors.Add(exception);
                    _logger.LogWarning(
                        exception,
                        "Ошибка подготовки push-доставки пользователю {UserId}",
                        participant.UserId);
                }
            }

            if (deliveryErrors.Count > 0)
            {
                throw new AggregateException(
                    "Не удалось доставить push всем получателям.",
                    deliveryErrors);
            }
        }

        private async Task<Guid> EnsureIdempotentNotificationAsync(
            Guid recipientId,
            string text,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            var bytes = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(idempotencyKey));
            var notificationId = new Guid(bytes[..16]);

            var exists = await _context.Notifications
                .AnyAsync(n => n.NotificationId == notificationId, cancellationToken);

            if (!exists)
            {
                _context.Notifications.Add(new Notification
                {
                    NotificationId = notificationId,
                    UserId = recipientId,
                    Text = _encryptionService.Encrypt(text),
                    CreationDate = UtcDateTime.Now,
                    Read = false
                });
                await _context.SaveChangesAsync(cancellationToken);
            }

            return notificationId;
        }

        private async Task<Guid> GetMessageIdAsync(
            Guid outboxId,
            CancellationToken cancellationToken)
        {
            return await _context.MessageNotificationOutbox
                .Where(row => row.OutboxId == outboxId)
                .Select(row => row.MessageId)
                .SingleAsync(cancellationToken);
        }

        private static string BuildPushIdempotencyKey(
            Guid outboxId,
            Guid recipientId,
            Guid subscriptionId)
        {
            return $"{outboxId:N}:{recipientId:N}:{subscriptionId:N}:push";
        }

        private static string BuildPushBody(string? messageText, bool hasAttachments)
        {
            if (!string.IsNullOrWhiteSpace(messageText))
            {
                var decrypted = _encryptionService.TryDecryptSafe(messageText);

                if (!decrypted.StartsWith("Encrypted:"))
                    return decrypted.Length > 60
                        ? decrypted[..57] + "..."
                        : decrypted;
            }

            return hasAttachments ? "Прикрепил файл" : "Новое сообщение";
        }

        private static object BuildPushPayload(
            string title,
            string body,
            string senderName,
            Guid chatId,
            bool isGroupChat,
            string groupName,
            Guid notificationId,
            bool notifySound,
            bool notifyPreview)
        {
            return new
            {
                title,
                body,
                sender = senderName,
                chatId = chatId.ToString(),
                chatType = isGroupChat ? "group" : "private",
                chatName = isGroupChat ? groupName : senderName,
                notificationId = notificationId.ToString(),
                silent = !notifySound,
                preview = notifyPreview
            };
        }


    }
}
