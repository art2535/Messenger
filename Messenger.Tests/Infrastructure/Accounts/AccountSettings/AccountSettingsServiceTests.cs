using Messenger.Application.DTOs.AccountSettings;
using Messenger.Core.Models;
using Messenger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

using Messenger.Tests.Shared.Fixtures;

namespace Messenger.Tests.Infrastructure.Accounts.AccountSettings;

[Collection(PostgreSqlCollection.Name)]
public sealed class AccountSettingsServiceTests
{
    private readonly PostgreSqlFixture _database;

    public AccountSettingsServiceTests(PostgreSqlFixture database) => _database = database;

    [Fact]
    public async Task GetByUserIdAsync_NormalizesMissingTheme_WithoutOverwritingPrivacyChoices()
    {
        var userId = Guid.NewGuid();
        await SeedUserAsync(userId);

        await using (var seed = _database.CreateContext())
        {
            var settings = await seed.AccountSettings.SingleAsync(x => x.AccountId == userId);
            settings.Theme = "";
            settings.ShowOnline = false;
            settings.ShowTyping = false;
            settings.ReadReceipts = false;
            await seed.SaveChangesAsync();
        }

        await using var context = _database.CreateContext();
        var service = new AccountSettingsService(context);
        var settingsDto = await service.GetByUserIdAsync(userId);

        Assert.Equal("system", settingsDto.Theme);
        Assert.False(settingsDto.ShowOnline);
        Assert.False(settingsDto.ShowTyping);
        Assert.False(settingsDto.ReadReceipts);
    }

    [Theory]
    [InlineData(" DARK ", "dark")]
    [InlineData("LIGHT", "light")]
    [InlineData("invalid-theme", "system")]
    [InlineData("", "system")]
    public async Task UpdateAsync_NormalizesThemeAndPersistsBooleanSettings(string theme, string expectedTheme)
    {
        var userId = Guid.NewGuid();
        await SeedUserAsync(userId);

        await using var context = _database.CreateContext();
        var service = new AccountSettingsService(context);
        var updated = await service.UpdateAsync(userId, new UpdateAccountSettingsRequest
        {
            Theme = theme,
            PushEnabled = false,
            ShowOnline = false,
            ReadReceipts = false,
            IdleLogout = false
        });

        Assert.Equal(expectedTheme, updated.Theme);
        Assert.False(updated.PushEnabled);
        Assert.False(updated.ShowOnline);
        Assert.False(updated.ReadReceipts);
        Assert.False(updated.IdleLogout);
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
