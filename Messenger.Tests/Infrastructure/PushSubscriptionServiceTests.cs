using Messenger.Application.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Messenger.Infrastructure.Persistence.Entities;
using Messenger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebPush;
using Xunit;

namespace Messenger.Tests.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class PushSubscriptionServiceTests
{
    private readonly PostgreSqlFixture _database;

    public PushSubscriptionServiceTests(PostgreSqlFixture database) => _database = database;

    [Fact]
    public async Task AddSubscriptionAsync_ConcurrentRegistrationsForSameEndpoint_LeaveOneOwner()
    {
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        const string endpoint = "https://push.example.test/subscription/unique-endpoint";

        await using (var seed = _database.CreateContext())
        {
            seed.Users.Add(CreateUser(firstUserId));
            seed.Users.Add(CreateUser(secondUserId));
            await seed.SaveChangesAsync();
        }

        await using var firstContext = _database.CreateContext();
        await using var secondContext = _database.CreateContext();
        var firstService = CreateService(firstContext);
        var secondService = CreateService(secondContext);

        await Task.WhenAll(
            firstService.AddSubscriptionAsync(firstUserId, CreateSubscription(endpoint, "key-a", "auth-a")),
            secondService.AddSubscriptionAsync(secondUserId, CreateSubscription(endpoint, "key-b", "auth-b")));

        await using var verify = _database.CreateContext();
        var rows = await verify.PushSubscriptions
            .Where(subscription => subscription.Endpoint == endpoint)
            .ToListAsync();

        Assert.Single(rows);
        Assert.Contains(rows[0].UserId, new[] { firstUserId, secondUserId });
    }

    [Fact]
    public async Task RemoveSubscriptionAsync_CannotDeleteAnotherUsersSubscription()
    {
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();

        await using (var seed = _database.CreateContext())
        {
            seed.Users.Add(CreateUser(ownerId));
            seed.Users.Add(CreateUser(otherUserId));
            seed.PushSubscriptions.Add(new Messenger.Infrastructure.Persistence.Entities.PushSubscription
            {
                Id = subscriptionId,
                UserId = ownerId,
                Endpoint = "https://push.example.test/subscription/owner",
                P256dh = "p256dh",
                Auth = "auth",
                CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
            await CreateService(context).RemoveSubscriptionAsync(otherUserId, subscriptionId);

        await using var verify = _database.CreateContext();
        Assert.True(await verify.PushSubscriptions.AnyAsync(subscription =>
            subscription.Id == subscriptionId && subscription.UserId == ownerId));
    }

    [Fact]
    public async Task UpdateSubscriptionAsync_CannotTakeEndpointOwnedByAnotherUser()
    {
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var ownSubscriptionId = Guid.NewGuid();
        const string otherEndpoint = "https://push.example.test/subscription/other";

        await using (var seed = _database.CreateContext())
        {
            seed.Users.Add(CreateUser(ownerId));
            seed.Users.Add(CreateUser(otherUserId));
            seed.PushSubscriptions.Add(new Messenger.Infrastructure.Persistence.Entities.PushSubscription
            {
                Id = ownSubscriptionId,
                UserId = ownerId,
                Endpoint = "https://push.example.test/subscription/own",
                P256dh = "p256dh-own",
                Auth = "auth-own",
                CreatedAt = DateTime.UtcNow
            });
            seed.PushSubscriptions.Add(new PushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = otherUserId,
                Endpoint = otherEndpoint,
                P256dh = "p256dh-other",
                Auth = "auth-other",
                CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var service = CreateService(context);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateSubscriptionAsync(
                ownerId,
                new Messenger.Application.DTOs.Push.UpdatePushSubscriptionDto
                {
                    Id = ownSubscriptionId,
                    Endpoint = otherEndpoint,
                    P256dh = "new-key",
                    Auth = "new-auth"
                }));
        }

        await using var verify = _database.CreateContext();
        Assert.Equal(2, await verify.PushSubscriptions.CountAsync());
        Assert.True(await verify.PushSubscriptions.AnyAsync(subscription =>
            subscription.Id == ownSubscriptionId && subscription.UserId == ownerId));
        Assert.True(await verify.PushSubscriptions.AnyAsync(subscription =>
            subscription.Endpoint == otherEndpoint && subscription.UserId == otherUserId));
    }

    private static User CreateUser(Guid userId) => new()
    {
        UserId = userId,
        FirstName = "Push",
        LastName = "Test",
        RegistrationDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Account = new AccountSetting { AccountId = userId }
    };

    private static Messenger.Application.DTOs.Push.CreatePushSubscriptionDto CreateSubscription(
        string endpoint, string p256dh, string auth) => new()
    {
        Endpoint = endpoint,
        P256dh = p256dh,
        Auth = auth
    };

    private static PushSubscriptionService CreateService(GuapMessengerContext context)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vapid:Subject"] = "mailto:tests@example.test",
                ["Vapid:PublicKey"] = "test-public-key",
                ["Vapid:PrivateKey"] = "test-private-key"
            })
            .Build();

        return new PushSubscriptionService(
            context,
            Mock.Of<IChatService>(),
            new WebPushClient(),
            configuration,
            Mock.Of<INotificationService>(),
            NullLogger<PushSubscriptionService>.Instance,
            Mock.Of<IEncryptionService>(),
            Mock.Of<IUserService>());
    }
}
