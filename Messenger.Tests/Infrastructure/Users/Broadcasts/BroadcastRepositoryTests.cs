using Messenger.Application.DTOs.Broadcasts;
using Messenger.Application.DTOs.Users;
using Messenger.Application.Files;
using Messenger.Core.Models;
using Messenger.Infrastructure.Services;
using Messenger.Infrastructure.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

using Messenger.Tests.Shared.Fixtures;

using Messenger.Tests.Shared.TestDoubles;

namespace Messenger.Tests.Infrastructure.Users.Broadcasts;

[Collection(PostgreSqlCollection.Name)]
public sealed class BroadcastRepositoryTests
{
    private readonly PostgreSqlFixture _database;

    public BroadcastRepositoryTests(PostgreSqlFixture database) => _database = database;









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
        new(context, new ConfigurationBuilder().Build(), CreateFileStorage(), new FileValidationService());

    private static LocalFileStorage CreateFileStorage() => new(
        Path.Combine(Path.GetTempPath(), "Messenger.Tests", Guid.NewGuid().ToString("N")));

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
