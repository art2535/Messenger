using Messenger.Core.Enums;
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


    [Fact]
    public async Task GetActivityStatsAsync_AggregatesUsersPresenceMessagesAndActiveChats()
    {
        await using var beforeContext = _database.CreateContext();
        var before = await new AdminService(beforeContext).GetActivityStatsAsync();

        var userId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        await using (var seed = _database.CreateContext())
        {
            seed.Users.Add(CreateUser(userId));
            seed.UserStatuses.Add(new UserStatus
            {
                UserId = userId,
                Online = true,
                LastActivity = DateTime.UtcNow
            });
            seed.Chats.Add(new Chat
            {
                ChatId = chatId,
                Name = "Stats test chat",
                Type = "group",
                UserId = userId,
                CreationDate = DateTime.UtcNow
            });
            seed.Messages.AddRange(Enumerable.Range(1, 20).Select(sequence => new Message
            {
                MessageId = Guid.NewGuid(),
                ChatId = chatId,
                SenderId = userId,
                MessageText = $"message-{sequence}",
                SendTime = DateTime.UtcNow,
                SequenceNumber = sequence,
                DeliveryStatus = MessageDeliveryStatus.Sent
            }));
            await seed.SaveChangesAsync();
        }

        await using var afterContext = _database.CreateContext();
        var after = await new AdminService(afterContext).GetActivityStatsAsync();

        Assert.Equal(before.TotalUsers + 1, after.TotalUsers);
        Assert.Equal(before.OnlineUsers + 1, after.OnlineUsers);
        Assert.Equal(before.MessagesLast24h + 20, after.MessagesLast24h);
        Assert.Equal(before.MessagesLast7d + 20, after.MessagesLast7d);
        Assert.Equal(before.ActiveChatsLast24h + 1, after.ActiveChatsLast24h);
        Assert.Contains(after.TopChats, chat => chat.ChatId == chatId && chat.MessageCount == 20);
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
