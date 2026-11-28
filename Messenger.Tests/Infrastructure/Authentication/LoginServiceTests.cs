using System.Security.Cryptography;
using System.Text;
using Messenger.Application.DTOs.Logins;
using Messenger.Core.Models;
using Messenger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

using Messenger.Tests.Shared.Fixtures;

namespace Messenger.Tests.Infrastructure.Authentication;

[Collection(PostgreSqlCollection.Name)]
public sealed class LoginServiceTests
{
    private readonly PostgreSqlFixture _database;

    public LoginServiceTests(PostgreSqlFixture database) => _database = database;

    [Fact]
    public async Task ExpireSessionAsync_UsesHashedSessionKey_AndRevokesOnlyMatchingActiveLogin()
    {
        var userId = Guid.NewGuid();
        var rawToken = Guid.NewGuid().ToString("N");
        var loginId = Guid.NewGuid();
        await SeedUserAsync(userId);
        await AddLoginAsync(userId, loginId, Hash(rawToken));

        await using (var context = _database.CreateContext())
        {
            var service = new LoginService(context);
            Assert.True(await service.IsSessionKeyActiveAsync(rawToken));
            Assert.True(await service.ExpireSessionAsync(rawToken));
            Assert.False(await service.ExpireSessionAsync(rawToken));
            Assert.Null(await service.IsSessionKeyActiveAsync(rawToken));
            Assert.Null(await service.IsSessionKeyActiveAsync(" "));
        }

        await using var verify = _database.CreateContext();
        var login = await verify.Logins.SingleAsync(x => x.LoginId == loginId);
        Assert.False(login.Active);
        Assert.Equal(string.Empty, login.Token);
        Assert.NotNull(login.LogoutTime);
    }

    [Fact]
    public async Task CloseLoginAsync_AndRevokeLoginAsync_AreOwnerScoped()
    {
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var loginId = Guid.NewGuid();
        await SeedUserAsync(ownerId);
        await SeedUserAsync(otherId);
        await AddLoginAsync(ownerId, loginId, "token-hash");

        await using var context = _database.CreateContext();
        var service = new LoginService(context);

        Assert.False(await service.CloseLoginAsync(otherId, loginId));
        Assert.False(await service.RevokeLoginAsync(otherId, loginId));
        Assert.True(await service.CloseLoginAsync(ownerId, loginId));
        Assert.False(await service.CloseLoginAsync(ownerId, loginId));
    }

    [Fact]
    public async Task RevokeOtherLoginsAsync_KeepsRequestedSessionAndOtherUsersSessions()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var keepId = Guid.NewGuid();
        var revokeId = Guid.NewGuid();
        var otherUsersLoginId = Guid.NewGuid();
        await SeedUserAsync(userId);
        await SeedUserAsync(otherUserId);
        await AddLoginAsync(userId, keepId, "keep");
        await AddLoginAsync(userId, revokeId, "revoke");
        await AddLoginAsync(otherUserId, otherUsersLoginId, "other");

        await using (var context = _database.CreateContext())
        {
            var service = new LoginService(context);
            Assert.Equal(1, await service.RevokeOtherLoginsAsync(userId, keepId));
        }

        await using var verify = _database.CreateContext();
        Assert.True((await verify.Logins.SingleAsync(x => x.LoginId == keepId)).Active);
        var revoked = await verify.Logins.SingleAsync(x => x.LoginId == revokeId);
        Assert.False(revoked.Active);
        Assert.Equal(string.Empty, revoked.Token);
        Assert.True((await verify.Logins.SingleAsync(x => x.LoginId == otherUsersLoginId)).Active);
    }

    [Fact]
    public async Task CloseActiveLoginsForIdleUsersAsync_RespectsIdleLogoutPreference()
    {
        var enabledId = Guid.NewGuid();
        var disabledId = Guid.NewGuid();
        await SeedUserAsync(enabledId);
        await SeedUserAsync(disabledId);

        await using (var seed = _database.CreateContext())
        {
            var disabledSettings = await seed.AccountSettings.SingleAsync(x => x.AccountId == disabledId);
            disabledSettings.IdleLogout = false;
            await seed.SaveChangesAsync();
        }

        await AddLoginAsync(enabledId, Guid.NewGuid(), "enabled");
        var disabledLoginId = Guid.NewGuid();
        await AddLoginAsync(disabledId, disabledLoginId, "disabled");

        await using (var context = _database.CreateContext())
        {
            var service = new LoginService(context);
            await service.CloseActiveLoginsForIdleUsersAsync(new[] { enabledId, disabledId });
        }

        await using var verify = _database.CreateContext();
        Assert.False((await verify.Logins.SingleAsync(x => x.UserId == enabledId)).Active);
        Assert.True((await verify.Logins.SingleAsync(x => x.LoginId == disabledLoginId)).Active);
    }

    private async Task SeedUserAsync(Guid userId)
    {
        await using var context = _database.CreateContext();
        context.Users.Add(new User
        {
            UserId = userId,
            FirstName = "Login",
            LastName = "Test",
            RegistrationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Account = new AccountSetting { AccountId = userId }
        });
        await context.SaveChangesAsync();
    }

    private async Task AddLoginAsync(Guid userId, Guid loginId, string tokenHash)
    {
        await using var context = _database.CreateContext();
        var service = new LoginService(context);
        await service.AddLoginAsync(new CreateLoginDto
        {
            LoginId = loginId,
            UserId = userId,
            TokenHash = tokenHash,
            IpAddress = "127.0.0.1",
            LoginTime = DateTime.UtcNow,
            Active = true
        });
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
