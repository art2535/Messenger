using System.Text.Json;
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
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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
        await using var scope = _scopeFactory.CreateAsyncScope();
        var scheduledService = scope.ServiceProvider.GetRequiredService<IScheduledMessageService>();
        var claimService = scope.ServiceProvider.GetRequiredService<IScheduledMessageClaimService>();
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
                // Сохраняем идентификатор, чтобы восстановление не создавало дубль.
                var candidateMessageId = Guid.NewGuid();
                var messageId = await claimService.TryClaimAsync(
                    item.ScheduledMessageId, candidateMessageId, now, ct);
                if (!messageId.HasValue)
                    continue;

                List<AttachmentInfo> attachments = new();
                if (!string.IsNullOrEmpty(item.AttachmentsJson))
                {
                    try
                    {
                        attachments = JsonSerializer.Deserialize<List<AttachmentInfo>>(item.AttachmentsJson, JsonOpts)
                                      ?? new List<AttachmentInfo>();
                    }
                    catch
                    {
                        attachments = new List<AttachmentInfo>();
                    }
                }

                string? senderName = null;
                try
                {
                    var user = await userService.GetUserByIdAsync(item.SenderId, ct);
                    if (user != null)
                        senderName = $"{user.FirstName} {user.LastName}".Trim();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(
                        ex,
                        "Failed to resolve sender for scheduled {Id}",
                        item.ScheduledMessageId);
                }

                var sentAt = DateTime.UtcNow;

                var payload = new ChatMessageSent
                {
                    MessageId = messageId.Value,
                    ChatId = item.ChatId,
                    SenderId = item.SenderId,
                    SenderName = string.IsNullOrWhiteSpace(senderName) ? "Пользователь" : senderName,
                    MessageText = item.MessageText,
                    SentAt = sentAt,
                    HasAttachments = item.HasAttachments || attachments.Count > 0,
                    Attachments = attachments,
                    ReplyToMessageId = item.ReplyToMessageId
                };

                await publishEndpoint.Publish(payload, ct);
                await scheduledService.MarkSentAsync(item.ScheduledMessageId, messageId.Value, ct);

                // Уведомляем отправителя, что запланированное ушло
                try
                {
                    var userGroup = $"User_{item.SenderId}";
                    await hub.Clients.Group(userGroup).SendAsync("ScheduledMessageSent", new
                    {
                        ScheduledMessageId = item.ScheduledMessageId,
                        MessageId = messageId.Value,
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
                    item.ScheduledMessageId, messageId.Value, item.ChatId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Оставляем Processing: после истечения lease запись безопасно восстановится.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch scheduled {Id}", item.ScheduledMessageId);
                try
                {
                    await scheduledService.MarkFailedAsync(
                        item.ScheduledMessageId,
                        ex.Message,
                        CancellationToken.None);
                }
                catch (Exception markEx)
                {
                    _logger.LogError(markEx, "Also failed to mark Failed for {Id}", item.ScheduledMessageId);
                }
            }
        }
    }
}
