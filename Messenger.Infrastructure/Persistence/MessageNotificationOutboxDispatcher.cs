using System.Text.Json;
using MassTransit;
using Messenger.Core.DTOs.Messages;
using Messenger.Core.Hubs;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Persistence;

public sealed class MessageNotificationOutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    ILogger<MessageNotificationOutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(2);
    private const int BatchSize = 20;
    private const int MaxAttempts = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Ошибка обработки Outbox уведомлений.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GuapMessengerContext>();
        await using var claimTransaction = await db.Database.BeginTransactionAsync(
            cancellationToken);
        var now = DateTime.UtcNow;
        var rows = await db.MessageNotificationOutbox
            .FromSqlInterpolated($"""
                SELECT * FROM "Message_Notification_Outbox"
                WHERE ("Status" = 'Pending' AND "Next_Attempt_At" <= {now})
                   OR ("Status" = 'Processing' AND "Locked_Until" <= {now})
                ORDER BY "Created_At"
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return;

        foreach (var row in rows)
        {
            row.Status = "Processing";
            row.LockedUntil = now.Add(LockDuration);
        }

        await db.SaveChangesAsync(cancellationToken);
        await claimTransaction.CommitAsync(cancellationToken);

        foreach (var row in rows)
        {
            try
            {
                await DeliverAsync(scope.ServiceProvider, row, cancellationToken);
                row.Status = "Delivered";
                row.DeliveredAt = DateTime.UtcNow;
                row.LockedUntil = null;
                row.LastError = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                row.AttemptCount++;
                row.LockedUntil = null;
                row.LastError = exception.Message.Length > 2000
                    ? exception.Message[..2000]
                    : exception.Message;

                if (row.AttemptCount >= MaxAttempts)
                {
                    row.Status = "DeadLetter";
                    logger.LogError(exception,
                        "Outbox уведомления {OutboxId} перемещён в DeadLetter.",
                        row.OutboxId);
                }
                else
                {
                    row.Status = "Pending";
                    var delaySeconds = Math.Min(3600, Math.Pow(2, row.AttemptCount));
                    row.NextAttemptAt = DateTime.UtcNow.AddSeconds(delaySeconds);
                    logger.LogWarning(exception,
                        "Ошибка доставки Outbox {OutboxId}. Попытка {Attempt}.",
                        row.OutboxId, row.AttemptCount);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task DeliverAsync(
        IServiceProvider services,
        MessageNotificationOutbox row,
        CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Deserialize<MessageDto>(row.Payload)
            ?? throw new InvalidOperationException("Не удалось прочитать DTO уведомления.");

        var hub = services.GetRequiredService<IHubContext<ChatHub>>();
        var chatService = services.GetRequiredService<IChatService>();
        var pushService = services.GetRequiredService<IPushSubscriptionService>();

        var chatGroup = row.ChatId.ToString();
        await hub.Clients.Group(chatGroup).SendAsync(
            "ReceiveMessage", message, cancellationToken);
        await hub.Clients.Group(chatGroup).SendAsync(
            "MessageSendingStatus",
            new
            {
                MessageId = row.MessageId,
                ChatId = row.ChatId,
                Status = "Sent",
                Timestamp = DateTimeOffset.UtcNow
            },
            cancellationToken);

        var chat = await chatService.GetChatByIdAsync(row.ChatId, cancellationToken);
        if (chat?.ChatParticipants != null)
        {
            foreach (var participant in chat.ChatParticipants)
            {
                await hub.Clients.Group($"User_{participant.UserId}")
                    .SendAsync("ReceiveMessage", message, cancellationToken);
            }
        }

        await pushService.SendPushToOfflineUsersAsync(
            row.ChatId,
            row.SenderId,
            row.SenderName,
            row.MessageText,
            row.HasAttachments,
            false,
            cancellationToken);
    }
}
