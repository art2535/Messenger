using Messenger.Application.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

using Messenger.Tests.Shared.Fixtures;

namespace Messenger.Tests.Infrastructure.Scheduling;

[Collection(PostgreSqlCollection.Name)]
public sealed class ScheduledMessageServiceTests
{
    private readonly PostgreSqlFixture _database;

    public ScheduledMessageServiceTests(PostgreSqlFixture database) => _database = database;

    [Fact]
    public async Task DispatchDueAsync_ConcurrentWorkers_SkipLockedRowsAndDispatchEachMessageOnce()
    {
        var userId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var seed = _database.CreateContext())
        {
            seed.Users.Add(new User
            {
                UserId = userId,
                FirstName = "Scheduled",
                LastName = "Sender",
                RegistrationDate = DateOnly.FromDateTime(now),
                Account = new AccountSetting { AccountId = userId }
            });
            seed.Chats.Add(new Chat
            {
                ChatId = chatId,
                Name = "Scheduled test",
                Type = "private",
                UserId = userId,
                CreationDate = now
            });
            seed.ScheduledMessages.AddRange(
                CreateScheduledMessage(firstId, chatId, userId, now.AddMinutes(-2)),
                CreateScheduledMessage(secondId, chatId, userId, now.AddMinutes(-1)));
            await seed.SaveChangesAsync();
        }

        var encryption = new Mock<IEncryptionService>().Object;
        await using var firstContext = _database.CreateContext();
        await using var secondContext = _database.CreateContext();
        var firstService = new ScheduledMessageService(
            firstContext, encryption, NullLogger<ScheduledMessageService>.Instance);
        var secondService = new ScheduledMessageService(
            secondContext, encryption, NullLogger<ScheduledMessageService>.Instance);

        var firstCallbackEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstCallback = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var firstWorker = firstService.DispatchDueAsync(async (_, ct) =>
        {
            firstCallbackEntered.TrySetResult();
            await releaseFirstCallback.Task.WaitAsync(ct);
            return Guid.NewGuid();
        }, now, limit: 1);

        await firstCallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var secondWorker = await secondService.DispatchDueAsync(
            (_, _) => Task.FromResult(Guid.NewGuid()),
            now,
            limit: 1);

        releaseFirstCallback.TrySetResult();
        var firstWorkerResult = await firstWorker;

        Assert.Single(firstWorkerResult);
        Assert.Single(secondWorker);
        Assert.NotEqual(
            firstWorkerResult[0].Message.ScheduledMessageId,
            secondWorker[0].Message.ScheduledMessageId);

        await using var verify = _database.CreateContext();
        var persisted = await verify.ScheduledMessages
            .Where(x => x.ScheduledMessageId == firstId || x.ScheduledMessageId == secondId)
            .ToListAsync();

        Assert.Equal(2, persisted.Count);
        Assert.All(persisted, message => Assert.Equal(ScheduledMessageStatus.Sent, message.Status));
        Assert.All(persisted, message => Assert.NotNull(message.ResultMessageId));
    }

    [Fact]
    public async Task DispatchDueAsync_WhenPublishingFails_RollsBackStatusChange()
    {
        var userId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var scheduledId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var seed = _database.CreateContext())
        {
            seed.Users.Add(new User
            {
                UserId = userId,
                FirstName = "Scheduled",
                LastName = "Sender",
                RegistrationDate = DateOnly.FromDateTime(now),
                Account = new AccountSetting { AccountId = userId }
            });
            seed.Chats.Add(new Chat
            {
                ChatId = chatId,
                Name = "Scheduled rollback test",
                Type = "private",
                UserId = userId,
                CreationDate = now
            });
            seed.ScheduledMessages.Add(CreateScheduledMessage(
                scheduledId, chatId, userId, now.AddMinutes(-1)));
            await seed.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var service = new ScheduledMessageService(
                context,
                new Mock<IEncryptionService>().Object,
                NullLogger<ScheduledMessageService>.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.DispatchDueAsync(
                    (_, _) => throw new InvalidOperationException("publish failed"),
                    now,
                    limit: 1));
        }

        await using var verify = _database.CreateContext();
        var persisted = await verify.ScheduledMessages
            .SingleAsync(x => x.ScheduledMessageId == scheduledId);

        Assert.Equal(ScheduledMessageStatus.Pending, persisted.Status);
        Assert.Null(persisted.ResultMessageId);
    }

    private static ScheduledMessage CreateScheduledMessage(
        Guid id, Guid chatId, Guid senderId, DateTime scheduledAt) => new()
    {
        ScheduledMessageId = id,
        ChatId = chatId,
        SenderId = senderId,
        MessageText = "encrypted payload",
        ScheduledAt = DateTime.SpecifyKind(scheduledAt, DateTimeKind.Utc),
        CreatedAt = DateTime.UtcNow,
        Status = ScheduledMessageStatus.Pending
    };
}
