using Messenger.Application.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

using Messenger.Tests.Shared.Fixtures;

namespace Messenger.Tests.Infrastructure.Notifications;

[Collection(PostgreSqlCollection.Name)]
public sealed class NotificationServiceTests
{
    private readonly PostgreSqlFixture _database;

    public NotificationServiceTests(PostgreSqlFixture database) => _database = database;

    [Fact]
    public async Task MarkAsReadAsync_DoesNotModifyAnotherUsersNotification_AndPreservesFirstReadAt()
    {
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();

        await using (var seed = _database.CreateContext())
        {
            seed.Users.Add(CreateUser(ownerId));
            seed.Users.Add(CreateUser(otherUserId));
            seed.Notifications.Add(new Notification
            {
                NotificationId = notificationId,
                UserId = ownerId,
                Text = "encrypted",
                CreationDate = DateTime.UtcNow,
                Read = false
            });
            await seed.SaveChangesAsync();
        }

        var encryption = CreateEncryptionService();
        await using (var context = _database.CreateContext())
        {
            var service = new NotificationService(context, encryption, NullLogger<NotificationService>.Instance);
            await service.MarkAsReadAsync(otherUserId, notificationId, CancellationToken.None);
        }

        DateTime firstReadAt;
        await using (var verify = _database.CreateContext())
        {
            var notification = await verify.Notifications.FindAsync(new object[] { notificationId }, CancellationToken.None);
            Assert.NotNull(notification);
            Assert.False(notification.Read);
            Assert.Null(notification.ReadAt);
        }

        await using (var context = _database.CreateContext())
        {
            var service = new NotificationService(context, encryption, NullLogger<NotificationService>.Instance);
            await service.MarkAsReadAsync(ownerId, notificationId, CancellationToken.None);
        }

        await using (var verify = _database.CreateContext())
        {
            var notification = await verify.Notifications.FindAsync(new object[] { notificationId }, CancellationToken.None);
            Assert.NotNull(notification);
            Assert.True(notification.Read);
            Assert.NotNull(notification.ReadAt);
            firstReadAt = notification.ReadAt.Value;
        }

        await Task.Delay(20);

        await using (var context = _database.CreateContext())
        {
            var service = new NotificationService(context, encryption, NullLogger<NotificationService>.Instance);
            await service.MarkAsReadAsync(ownerId, notificationId, CancellationToken.None);
        }

        await using (var verify = _database.CreateContext())
        {
            var notification = await verify.Notifications.FindAsync(new object[] { notificationId }, CancellationToken.None);
            Assert.NotNull(notification);
            Assert.Equal(firstReadAt, notification.ReadAt);
        }
    }

    [Fact]
    public async Task CreateNotificationAsync_ConcurrentCallsWithSameKey_ReturnSameNotification()
    {
        var userId = Guid.NewGuid();
        await using (var seed = _database.CreateContext())
        {
            seed.Users.Add(CreateUser(userId));
            await seed.SaveChangesAsync();
        }

        var encryption = CreateEncryptionService();
        await using var firstContext = _database.CreateContext();
        await using var secondContext = _database.CreateContext();
        var firstService = new NotificationService(firstContext, encryption, NullLogger<NotificationService>.Instance);
        var secondService = new NotificationService(secondContext, encryption, NullLogger<NotificationService>.Instance);

        var results = await Task.WhenAll(
            firstService.CreateNotificationAsync(userId, "hello", "same-event-key", CancellationToken.None),
            secondService.CreateNotificationAsync(userId, "hello", "same-event-key", CancellationToken.None));

        Assert.Equal(results[0], results[1]);

        await using var verify = _database.CreateContext();
        Assert.Equal(1, await verify.Notifications.CountAsync(n => n.DeduplicationKey == "same-event-key", CancellationToken.None));
    }

    [Fact]
    public async Task CreateNotificationAsync_RejectsDeduplicationKeyOwnedByAnotherUser()
    {
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        await using (var seed = _database.CreateContext())
        {
            seed.Users.Add(CreateUser(ownerId));
            seed.Users.Add(CreateUser(otherUserId));
            await seed.SaveChangesAsync();
        }

        var encryption = CreateEncryptionService();
        Guid originalId;
        await using (var context = _database.CreateContext())
        {
            var service = new NotificationService(context, encryption, NullLogger<NotificationService>.Instance);
            originalId = await service.CreateNotificationAsync(ownerId, "hello", "cross-user-key", CancellationToken.None);
        }

        await using var otherContext = _database.CreateContext();
        var otherService = new NotificationService(otherContext, encryption, NullLogger<NotificationService>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => otherService.CreateNotificationAsync(otherUserId, "hello", "cross-user-key", CancellationToken.None));

        await using var verify = _database.CreateContext();
        Assert.Equal(1, await verify.Notifications.CountAsync(n => n.DeduplicationKey == "cross-user-key", CancellationToken.None));
        Assert.True(await verify.Notifications.AnyAsync(
            n => n.NotificationId == originalId && n.UserId == ownerId,
            CancellationToken.None));
    }

    private static User CreateUser(Guid userId) => new()
    {
        UserId = userId,
        FirstName = "Test",
        LastName = "User",
        RegistrationDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Account = new AccountSetting { AccountId = userId }
    };

    private static IEncryptionService CreateEncryptionService()
    {
        var key = Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
        return new AesGcmEncryptionService(Options.Create(new AesGcmEncryptionOptions
        {
            MasterKeyBase64 = key
        }));
    }
}
