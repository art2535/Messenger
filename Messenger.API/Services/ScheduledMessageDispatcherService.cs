using MassTransit;
using Messenger.Core.Interfaces;
using Messenger.Core.Messages;
using Messenger.Core.Models;
using Microsoft.AspNetCore.SignalR;
using Messenger.Core.Hubs;

namespace Messenger.API.Services;

/// <summary>
/// Фоновый сервис: раз в несколько секунд выбирает due ScheduledMessage
/// и публикует их в тот же pipeline, что и обычные сообщения (ChatMessageSent → MassTransit → Consumer).
/// </summary>
public class ScheduledMessageDispatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledMessageDispatcherService> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
public ScheduledMessageDispatcherService(IServiceScopeFactory scopeFactory,
        ILogger<ScheduledMessageDispatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScheduledMessageDispatcherService started");

        // Небольшая задержка при старте, чтобы БД и MassTransit успели подняться
        await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ScheduledMessageDispatcher iteration failed");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("ScheduledMessageDispatcherService stopped");
    }

    private async Task ProcessDueAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var scheduledService = scope.ServiceProvider.GetRequiredService<IScheduledMessageService>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ChatHub>>();

        var now = DateTime.UtcNow;
        var due = await scheduledService.GetDueAsync(now, 50, ct);
        if (due.Count == 0)
            return;

        _logger.LogInformation("Found {Count} due scheduled message(s)", due.Count);

        foreach (var item in due)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // Optimistic lock: помечаем как обрабатываемые через повторную проверку статуса
                var fresh = await scheduledService.GetByIdAsync(item.ScheduledMessageId, ct);
                if (fresh == null || fresh.Status != ScheduledMessageStatus.Pending)
                    continue;

                var attachments = item.Attachments;

                string? senderName = null;
                try
                {
                    var user = await userService.GetUserByIdAsync(item.SenderId, ct);
                    if (user != null)
                        senderName = $"{user.FirstName} {user.LastName}".Trim();
                }
                catch { /* ignore */ }

                var messageId = Guid.NewGuid();
                var sentAt = DateTime.UtcNow;

                var payload = new ChatMessageSent
                {
                    MessageId = messageId,
                    ChatId = item.ChatId,
                    SenderId = item.SenderId,
                    SenderName = string.IsNullOrWhiteSpace(senderName) ? "Пользователь" : senderName,
                    MessageText = item.EncryptedMessageText,
                    SentAt = sentAt,
                    HasAttachments = item.HasAttachments || attachments.Count > 0,
                    Attachments = attachments,
                    ReplyToMessageId = item.ReplyToMessageId
                };

                await publishEndpoint.Publish(payload, ct);
                await scheduledService.MarkSentAsync(item.ScheduledMessageId, messageId, ct);

                // Уведомляем отправителя, что запланированное ушло
                try
                {
                    var userGroup = $"User_{item.SenderId}";
                    await hub.Clients.Group(userGroup).SendAsync("ScheduledMessageSent", new
                    {
                        ScheduledMessageId = item.ScheduledMessageId,
                        MessageId = messageId,
                        ChatId = item.ChatId,
                        ScheduledAt = item.ScheduledAt,
                        SentAt = sentAt
                    }, ct);
                }
                catch (Exception sigEx)
                {
                    _logger.LogWarning(sigEx, "SignalR ScheduledMessageSent failed for {Id}", item.ScheduledMessageId);
                }

                _logger.LogInformation(
                    "Scheduled {ScheduledId} → Message {MessageId} published for chat {ChatId}",
                    item.ScheduledMessageId, messageId, item.ChatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch scheduled {Id}", item.ScheduledMessageId);
                try
                {
                    await scheduledService.MarkFailedAsync(item.ScheduledMessageId, ex.Message, ct);
                }
                catch (Exception markEx)
                {
                    _logger.LogError(markEx, "Also failed to mark Failed for {Id}", item.ScheduledMessageId);
                }
            }
        }
    }
}
