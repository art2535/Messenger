using Messenger.Application.DTOs.Broadcasts;
using Messenger.Core.Models;
using Messenger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Messenger.Tests.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class UserAndBroadcastServiceTests
{
    private readonly PostgreSqlFixture _database;

    public UserAndBroadcastServiceTests(PostgreSqlFixture database) => _database = database;

    [Fact]
    public async Task RegisterExternalUserAsync_ConcurrentCalls_ReturnOnePersistedIdentity()
    {
        const string externalId = "external-concurrent-user";
        await using var firstContext = _database.CreateContext();
        await using var secondContext = _database.CreateContext();
        var firstService = CreateUserService(firstContext);
        var secondService = CreateUserService(secondContext);

        var results = await Task.WhenAll(
            firstService.RegisterExternalUserAsync(externalId, "first@example.test", "First", "User"),
            secondService.RegisterExternalUserAsync(externalId, "second@example.test", "Second", "User"));

        Assert.Equal(results[0].UserId, results[1].UserId);
        Assert.Equal(externalId, results[0].ExternalId);

        await using var verify = _database.CreateContext();
        var users = await verify.Users.Where(x => x.ExternalId == externalId).ToListAsync();
        Assert.Single(users);
        Assert.True(await verify.AccountSettings.AnyAsync(x => x.AccountId == users[0].UserId));
        Assert.True(await verify.UserStatuses.AnyAsync(x => x.UserId == users[0].UserId));
        Assert.Equal(1, await verify.Users.CountAsync(x => x.ExternalId == externalId));
    }

    [Fact]
    public async Task RegisterExternalUserAsync_NormalizesExternalIdAndAssignsDefaultRole()
    {
        await using var context = _database.CreateContext();
        var service = CreateUserService(context);

        var user = await service.RegisterExternalUserAsync("  provider|subject-42  ",
            "user@example.test", "Test", "Person");

        Assert.Equal("provider|subject-42", user.ExternalId);
        Assert.Contains(user.Roles, role => role.Name == "Пользователь");
        Assert.Equal(user.UserId, (await service.GetUserByExternalIdAsync("provider|subject-42"))?.UserId);

    }

    [Fact]
    public async Task RegisterExternalUserAsync_RejectsInvalidExternalId()
    {
        await using var context = _database.CreateContext();
        var service = CreateUserService(context);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RegisterExternalUserAsync(" ", "user@example.test", "Test", "Person"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RegisterExternalUserAsync(new string('x', 256), "user@example.test", "Test", "Person"));
    }

    [Fact]
    public async Task AssignRoleAsync_IsIdempotentForRepeatedAssignments()
    {
        var userId = Guid.NewGuid();
        await SeedUserAsync(userId);

        await using var context = _database.CreateContext();
        var service = CreateUserService(context);
        var roleId = await context.Roles.Where(x => x.Name == "Студент").Select(x => x.RoleId).SingleAsync();

        await service.AssignRoleAsync(userId, roleId);
        await service.AssignRoleAsync(userId, roleId);

        Assert.Equal(1, await context.Set<Dictionary<string, object>>("UserRole")
            .CountAsync(x => (Guid)x["UserId"] == userId && (Guid)x["RoleId"] == roleId));
    }

    [Fact]
    public async Task BroadcastRepository_CreatesRecipientRowsAndEnforcesRecipientOwnership()
    {
        var senderId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await SeedUserAsync(senderId);
        await SeedUserAsync(recipientId);
        await SeedUserAsync(otherId);

        await using var context = _database.CreateContext();
        var repository = new EfBroadcastRepository(context);
        var existing = await repository.GetExistingRecipientIdsAsync(new[] { recipientId, otherId, Guid.NewGuid() });
        Assert.Equal(2, existing.Count);

        var created = await repository.CreateBroadcastAsync(new CreateBroadcastRequest
        {
            Title = "Maintenance",
            MessageText = "Service window",
            RecipientIds = new List<Guid> { recipientId, otherId }
        }, senderId, new[] { recipientId, otherId });

        Assert.Equal(2, created.TotalRecipients);
        Assert.NotEqual(Guid.Empty, created.BroadcastId);

        Assert.Null(await repository.MarkAsReadAsync(created.BroadcastId, senderId));
        var marked = await repository.MarkAsReadAsync(created.BroadcastId, recipientId);
        Assert.NotNull(marked);
        Assert.True(marked.Success);
        Assert.NotNull(marked.ReadAt);

        var originalReadAt = marked.ReadAt;
        await Task.Delay(20);
        var markedAgain = await repository.MarkAsReadAsync(created.BroadcastId, recipientId);
        Assert.Equal(originalReadAt, markedAgain?.ReadAt);

        var unread = await repository.GetMyBroadcastsAsync(recipientId, unreadOnly: true);
        Assert.DoesNotContain(unread, item => item.BroadcastId == created.BroadcastId);

        var summary = await repository.GetBroadcastSummaryAsync(created.BroadcastId);
        Assert.NotNull(summary);
        Assert.Equal(1, summary.ReadCount);
        Assert.Equal(2, summary.TotalRecipients);
    }

    private UserService CreateUserService(Messenger.Infrastructure.Data.GuapMessengerContext context) =>
        new(context, new ConfigurationBuilder().Build());

    private async Task SeedUserAsync(Guid userId)
    {
        await using var context = _database.CreateContext();
        context.Users.Add(CreateUser(userId));
        await context.SaveChangesAsync();
    }

    private static User CreateUser(Guid userId) => new()
    {
        UserId = userId,
        FirstName = "Repository",
        LastName = "Test",
        RegistrationDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Account = new AccountSetting { AccountId = userId }
    };
}
