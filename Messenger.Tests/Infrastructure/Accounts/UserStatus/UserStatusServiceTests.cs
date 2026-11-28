using Messenger.Application.DTOs.AccountSettings;
using Messenger.Core.Models;
using UserStatusModel = Messenger.Core.Models.UserStatus;
using Messenger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

using Messenger.Tests.Shared.Fixtures;

namespace Messenger.Tests.Infrastructure.Accounts.UserStatus;

[Collection(PostgreSqlCollection.Name)]
public sealed class UserStatusServiceTests
{
    private readonly PostgreSqlFixture _database;

    public UserStatusServiceTests(PostgreSqlFixture database) => _database = database;





    [Fact]
    public async Task UpdateUserStatusAsync_ConcurrentUpserts_LeaveSingleLatestStatusRow()
    {
        var userId = Guid.NewGuid();
        await SeedUserAsync(userId);

        await using var first = _database.CreateContext();
        await using var second = _database.CreateContext();
        var firstService = new UserStatusService(first);
        var secondService = new UserStatusService(second);

        await Task.WhenAll(
            firstService.UpdateUserStatusAsync(userId, true),
            secondService.UpdateUserStatusAsync(userId, false));

        await using var verify = _database.CreateContext();
        var statuses = await verify.UserStatuses.Where(x => x.UserId == userId).ToListAsync();
        Assert.Single(statuses);
        Assert.NotNull(statuses[0].LastActivity);
    }

    [Fact]
    public async Task SetOnlineAndOfflineBatchAsync_OnlyChangesRequestedUsers()
    {
        var onlineId = Guid.NewGuid();
        var offlineId = Guid.NewGuid();
        var untouchedId = Guid.NewGuid();

        await using (var seed = _database.CreateContext())
        {
            seed.Users.AddRange(CreateUser(onlineId), CreateUser(offlineId), CreateUser(untouchedId));
            seed.UserStatuses.AddRange(
                new UserStatusModel { UserId = onlineId, Online = false },
                new UserStatus { UserId = offlineId, Online = true },
                new UserStatus { UserId = untouchedId, Online = false });
            await seed.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var service = new UserStatusService(context);
            await service.SetOnlineBatchAsync(new[] { onlineId, onlineId });
            await service.SetOfflineBatchAsync(new[] { offlineId });
            await service.UpdateActivityAsync(onlineId);
        }

        await using var verify = _database.CreateContext();
        Assert.True((await verify.UserStatuses.SingleAsync(x => x.UserId == onlineId)).Online);
        Assert.False((await verify.UserStatuses.SingleAsync(x => x.UserId == offlineId)).Online);
        Assert.False((await verify.UserStatuses.SingleAsync(x => x.UserId == untouchedId)).Online);
        Assert.Empty(await new UserStatusService(verify).GetInactiveOnlineStatusesAsync(DateTime.UtcNow.AddMinutes(-1)));
    }

    private async Task SeedUserAsync(Guid userId)
    {
        await using var context = _database.CreateContext();
        context.Users.Add(CreateUser(userId));
        await context.SaveChangesAsync();
    }

    private static User CreateUser(Guid userId) => new()
    {
        UserId = userId,
        FirstName = "Service",
        LastName = "Test",
        RegistrationDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Account = new AccountSetting { AccountId = userId }
    };
}
