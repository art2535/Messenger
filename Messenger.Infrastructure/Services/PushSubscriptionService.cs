using Messenger.Application.Common;
using Messenger.Application.DTOs.Push;
using Messenger.Application.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text.Json;
using WebPush;
using PushSubscriptionEntity = Messenger.Infrastructure.Persistence.Entities.PushSubscription;

namespace Messenger.Infrastructure.Services
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

        public async Task<List<PushSubscriptionDto>> GetSubscriptionsByUserIdAsync(Guid userId, CancellationToken ct = default)
        {
            return await _context.PushSubscriptions
                .AsNoTracking()
                .Where(s => s.UserId == userId)
                .Select(s => new PushSubscriptionDto
                {
                    Id = s.Id,
                    UserId = s.UserId,
                    Endpoint = s.Endpoint,
                    P256dh = s.P256dh,
                    Auth = s.Auth,
                    CreatedAt = s.CreatedAt,
                    LastUsedAt = s.LastUsedAt
                })
                .ToListAsync(ct);
        }

        public async Task AddSubscriptionAsync(Guid userId, CreatePushSubscriptionDto subscription, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(subscription);
            if (string.IsNullOrWhiteSpace(subscription.Endpoint)
                || string.IsNullOrWhiteSpace(subscription.P256dh)
                || string.IsNullOrWhiteSpace(subscription.Auth))
                throw new ArgumentException("Push-подписка содержит пустые обязательные поля", nameof(subscription));

            // Endpoint идентифицирует конкретную browser subscription. Сериализуем
            // регистрацию одного endpoint и переносим его к текущему владельцу.
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({subscription.Endpoint}, 0))", ct);

            await _context.PushSubscriptions
                .Where(s => s.Endpoint == subscription.Endpoint)
                .ExecuteDeleteAsync(ct);

            _context.PushSubscriptions.Add(new PushSubscriptionEntity
            {
                UserId = userId,
                Endpoint = subscription.Endpoint,
                P256dh = subscription.P256dh,
                Auth = subscription.Auth,
                CreatedAt = UtcDateTime.Now,
                LastUsedAt = UtcDateTime.Now
            });
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        public async Task EnsurePushEnabledAsync(Guid userId, CancellationToken token = default)
        {
            var settings = await _context.AccountSettings
                .FirstOrDefaultAsync(s => s.AccountId == userId, token);

            if (settings == null)
            {
                settings = new AccountSetting
                {
                    AccountId = userId,
                    PushEnabled = true,
                    NotifyMessages = true,
                    NotifyGroupChats = true,
                    NotifyMentions = true
                };
                _context.AccountSettings.Add(settings);
                try
                {
                    await _context.SaveChangesAsync(token);
                    return;
                }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                    { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    _context.Entry(settings).State = EntityState.Detached;
                    settings = await _context.AccountSettings
                        .FirstOrDefaultAsync(s => s.AccountId == userId, token)
                        ?? throw new InvalidOperationException("Не удалось получить настройки push после конкурентного создания");
                }
            }

            if (!settings.PushEnabled)
            {
                settings.PushEnabled = true;
                await _context.SaveChangesAsync(token);
            }
        }

        public async Task RemoveSubscriptionAsync(Guid userId, Guid id, CancellationToken ct = default)
        {
            await _context.PushSubscriptions
                .Where(s => s.Id == id && s.UserId == userId)
                .ExecuteDeleteAsync(ct);
        }

        public async Task UpdateSubscriptionAsync(Guid userId, UpdatePushSubscriptionDto subscription, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(subscription);
            if (string.IsNullOrWhiteSpace(subscription.Endpoint)
                || string.IsNullOrWhiteSpace(subscription.P256dh)
                || string.IsNullOrWhiteSpace(subscription.Auth))
                throw new ArgumentException("Push-подписка содержит пустые обязательные поля", nameof(subscription));

            await using var transaction = await _context.Database.BeginTransactionAsync(ct);
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({subscription.Endpoint}, 0))", ct);

            var ownsSubscription = await _context.PushSubscriptions
                .AnyAsync(s => s.Id == subscription.Id && s.UserId == userId, ct);
            if (!ownsSubscription)
            {
                await transaction.CommitAsync(ct);
                return;
            }

            var conflictingOwner = await _context.PushSubscriptions
                .Where(s => s.Endpoint == subscription.Endpoint && s.Id != subscription.Id)
                .Select(s => (Guid?)s.UserId)
                .FirstOrDefaultAsync(ct);
            if (conflictingOwner.HasValue && conflictingOwner.Value != userId)
                throw new InvalidOperationException("Endpoint уже принадлежит другой учётной записи");

            await _context.PushSubscriptions
                .Where(s => s.Endpoint == subscription.Endpoint && s.Id != subscription.Id && s.UserId == userId)
                .ExecuteDeleteAsync(ct);

            await _context.PushSubscriptions
                .Where(s => s.Id == subscription.Id && s.UserId == userId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.Endpoint, subscription.Endpoint)
                    .SetProperty(s => s.P256dh, subscription.P256dh)
                    .SetProperty(s => s.Auth, subscription.Auth)
                    .SetProperty(s => s.LastUsedAt, UtcDateTime.Now), ct);

            await transaction.CommitAsync(ct);
        }

        public async Task RemoveByEndpointAsync(Guid userId, string endpoint, CancellationToken ct = default)
        {
            await _context.PushSubscriptions
                .Where(s => s.UserId == userId && s.Endpoint == endpoint)
                .ExecuteDeleteAsync(ct);
        }

        public async Task<PushSettingsDto?> GetPushSettingsAsync(Guid userId, CancellationToken token = default)
        {
            return await _context.AccountSettings
                .AsNoTracking()
                .Where(s => s.AccountId == userId)
                .Select(s => new PushSettingsDto
                {
                    PushEnabled = s.PushEnabled,
                    NotifyMessages = s.NotifyMessages,
                    NotifyGroupChats = s.NotifyGroupChats,
                    NotifyMentions = s.NotifyMentions
                })
                .FirstOrDefaultAsync(token);
        }

        public async Task SavePushSettingsAsync(Guid userId, UpdatePushSettingsDto request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var settings = await _context.AccountSettings
                .FirstOrDefaultAsync(s => s.AccountId == userId, token);
            var isNew = settings == null;
            settings ??= new AccountSetting { AccountId = userId };

            settings.PushEnabled = request.PushEnabled;
            settings.NotifyMessages = request.NotifyMessages;
            settings.NotifyGroupChats = request.NotifyGroupChats;
            settings.NotifyMentions = request.NotifyMentions;

            if (isNew)
                _context.AccountSettings.Add(settings);

            await using var transaction = await _context.Database.BeginTransactionAsync(token);
            try
            {
                await _context.SaveChangesAsync(token);
                if (!request.PushEnabled)
                {
                    await _context.PushSubscriptions
                        .Where(s => s.UserId == userId)
                        .ExecuteDeleteAsync(token);
                    _logger.LogInformation("Все push-подписки удалены для пользователя {UserId}", userId);
                }
                await transaction.CommitAsync(token);
            }
            catch (DbUpdateException ex) when (isNew && ex.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await transaction.RollbackAsync(token);
                await transaction.DisposeAsync();
                _context.Entry(settings).State = EntityState.Detached;
                settings = await _context.AccountSettings
                    .FirstOrDefaultAsync(s => s.AccountId == userId, token)
                    ?? throw new InvalidOperationException("Не удалось получить настройки push после конкурентного создания");

                settings.PushEnabled = request.PushEnabled;
                settings.NotifyMessages = request.NotifyMessages;
                settings.NotifyGroupChats = request.NotifyGroupChats;
                settings.NotifyMentions = request.NotifyMentions;
                await using var retryTransaction = await _context.Database.BeginTransactionAsync(token);
                await _context.SaveChangesAsync(token);
                if (!request.PushEnabled)
                    await _context.PushSubscriptions.Where(s => s.UserId == userId).ExecuteDeleteAsync(token);
                await retryTransaction.CommitAsync(token);
            }
        }

        public async Task SendPushForMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            try
            {
                var message = await _context.Messages
                    .AsNoTracking()
                    .Where(m => m.MessageId == messageId)
                    .Select(m => new
                    {
                        m.ChatId,
                        m.SenderId,
                        m.MessageText,
                        m.HasAttachments
                    })
                    .FirstOrDefaultAsync(cancellationToken);

                // Push разрешён только для уже сохранённого сообщения.
                if (message == null)
                    return;

                var chatId = message.ChatId;
                var senderId = message.SenderId;
                var messageText = message.MessageText;
                var hasAttachments = message.HasAttachments;
                const bool isMention = false;

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

                // Чат и отправитель дополнительно проверяются через actor-aware API.
                var chat = await _chatService.GetChatByIdAsync(chatId, senderId, cancellationToken);
                if (chat == null)
                    return;

                bool isGroupChat = string.Equals(chat.Type, "group", StringComparison.OrdinalIgnoreCase);
                var participants = (await _chatService.GetChatParticipantsAsync(chatId, senderId, cancellationToken)).ToList();
                var sender = participants.FirstOrDefault(participant => participant.UserId == senderId)
                    ?? throw new UnauthorizedAccessException("Отправитель не является участником чата");
                senderName = $"{sender.FirstName} {sender.LastName}".Trim();
                if (string.IsNullOrWhiteSpace(senderName))
                    senderName = "Пользователь";

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
                    bool notifyPreview = true;
                    bool notifySound = true;

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

                        var notificationId = messageId.HasValue
                            ? await _notificationService.CreateNotificationAsync(
                                participant.UserId,
                                notificationText,
                                $"chat-message:{messageId.Value:N}:recipient:{participant.UserId:N}",
                                cancellationToken)
                            : await _notificationService.CreateNotificationAsync(
                                participant.UserId, notificationText, cancellationToken);

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
                                    silent = !notifySound,
                                    preview = notifyPreview
                                };

                                await _webPushClient.SendNotificationAsync(pushSubscription, JsonSerializer.Serialize(payload),
                                    _vapidDetails, cancellationToken);

                                var subEntity = await _context.PushSubscriptions.FindAsync(new object[] { sub.Id }, cancellationToken);
                                if (subEntity != null)
                                {
                                    subEntity.LastUsedAt = UtcDateTime.Now;
                                    await _context.SaveChangesAsync(cancellationToken);
                                }
                            }
                            catch (WebPushException webEx) when (webEx.Message.Contains("no longer valid") ||
                                                                 webEx.Message.Contains("unsubscribed") ||
                                                                 webEx.Message.Contains("expired"))
                            {
                                _logger.LogWarning("Подписка пользователя {UserId} устарела. Удаляем...", participant.UserId);
                                await _context.PushSubscriptions
                                    .Where(s => s.Id == sub.Id && s.UserId == participant.UserId)
                                    .ExecuteDeleteAsync(cancellationToken);
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Временная ошибка отправки push пользователю {UserId}; событие будет повторено", participant.UserId);
                                throw;
                            }
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Ошибка при создании уведомления или отправке push для пользователя {UserId}; событие будет повторено", participant.UserId);
                        throw;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Общая ошибка при отправке push для чата {ChatId}", chatId);
                throw;
            }
        }
    }
}
