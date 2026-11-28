using Messenger.Core.Models;
using Messenger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Messenger.Tests.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class AdminServiceTests
{
    private readonly PostgreSqlFixture _database;

    public AdminServiceTests(PostgreSqlFixture database) => _database = database;

    [Fact]
    public async Task IsSystemAdminAsync_UsesPersistedRoleAndReturnsFalseForMissingOrRegularUsers()
    {
        var adminId = Guid.NewGuid();
        var regularId = Guid.NewGuid();

        await using (var seed = _database.CreateContext())
        {
            seed.Users.AddRange(CreateUser(adminId), CreateUser(regularId));
            await seed.SaveChangesAsync();

            var admin = await seed.Users.SingleAsync(x => x.UserId == adminId);
            var adminRole = await seed.Roles.SingleAsync(x => x.Name == "Администратор");
            admin.Roles.Add(adminRole);
            await seed.SaveChangesAsync();
        }

        await using var context = _database.CreateContext();
        var service = new AdminService(context);

        Assert.True(await service.IsSystemAdminAsync(adminId));
        Assert.False(await service.IsSystemAdminAsync(regularId));
        Assert.False(await service.IsSystemAdminAsync(Guid.NewGuid()));
    }

    private static User CreateUser(Guid userId) => new()
    {
        UserId = userId,
        FirstName = "Admin",
        LastName = "Test",
        RegistrationDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Account = new AccountSetting { AccountId = userId }
    };
}
