using MassTransit;
using Messenger.Application.Interfaces;
using Messenger.Application.Messages;
using Messenger.Realtime.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.API.Services;

/// <summary>
/// Диспетчер использует PostgreSQL row locks и MassTransit transactional Bus Outbox.
/// </summary>
public sealed class ScheduledMessageDispatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledMessageDispatcherService> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    public ScheduledMessageDispatcherService(
        IServiceScopeFactory scopeFactory,
        ILogger<ScheduledMessageDispatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScheduledMessageDispatcherService started");
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
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

        var dispatched = await scheduledService.DispatchDueAsync(
            async (item, token) =>
            {
                string? senderName = null;
                try
                {
                    var user = await userService.GetUserByIdAsync(item.SenderId, token);
                    if (user != null)
                        senderName = $"{user.FirstName} {user.LastName}".Trim();
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Не удалось получить отправителя запланированного сообщения {Id}",
                        item.ScheduledMessageId);
                }

                var messageId = Guid.NewGuid();
                var payload = new ChatMessageSent
                {
                    MessageId = messageId,
                    ChatId = item.ChatId,
                    SenderId = item.SenderId,
                    SenderName = string.IsNullOrWhiteSpace(senderName) ? "Пользователь" : senderName,
                    MessageText = item.EncryptedMessageText,
                    SentAt = DateTime.UtcNow,
                    HasAttachments = item.HasAttachments || item.Attachments.Count > 0,
                    Attachments = item.Attachments,
                    ReplyToMessageId = item.ReplyToMessageId
                };

                // UseBusOutbox сохраняет публикацию в том же scoped DbContext.
                await publishEndpoint.Publish(payload, token);
                return messageId;
            },
            DateTime.UtcNow, 50, ct);

        foreach (var result in dispatched)
        {
            var item = result.Message;
            try
            {
                await hub.Clients.Group($"User_{item.SenderId}")
                    .SendAsync("ScheduledMessageSent", new
                    {
                        ScheduledMessageId = item.ScheduledMessageId,
                        MessageId = result.ResultMessageId,
                        ChatId = item.ChatId,
                        ScheduledAt = item.ScheduledAt,
                        SentAt = DateTime.UtcNow
                    }, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SignalR ScheduledMessageSent failed for {Id}", item.ScheduledMessageId);
            }

            _logger.LogInformation(
                "Scheduled {ScheduledId} → Message {MessageId} queued in outbox for chat {ChatId}",
                item.ScheduledMessageId, result.ResultMessageId, item.ChatId);
        }

        if (dispatched.Count > 0)
            _logger.LogInformation("Queued {Count} scheduled message(s) in transactional outbox", dispatched.Count);
    }
}
