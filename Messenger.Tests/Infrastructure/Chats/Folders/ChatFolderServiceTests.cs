using Messenger.Core.Enums;
using Messenger.Core.Models;
using Messenger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

using Messenger.Tests.Shared.Fixtures;

namespace Messenger.Tests.Infrastructure.Chats.Folders;

[Collection(PostgreSqlCollection.Name)]
public sealed class ChatFolderServiceTests
{
    private readonly PostgreSqlFixture _database;

    public ChatFolderServiceTests(PostgreSqlFixture database) => _database = database;

    [Fact]
    public async Task AddChatAsync_ConcurrentCalls_AddOnlyOneFolderChat()
    {
        var userId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var folderId = Guid.NewGuid();

        await using (var seed = _database.CreateContext())
        {
            seed.Users.Add(CreateUser(userId));
            seed.Chats.Add(new Chat
            {
                ChatId = chatId,
                Name = "Folder concurrency",
                Type = "private",
                UserId = userId,
                CreationDate = DateTime.UtcNow
            });
            seed.ChatParticipants.Add(new ChatParticipant
            {
                ChatId = chatId,
                UserId = userId,
                Role = "participant",
                JoinDate = DateTime.UtcNow
            });
            seed.ChatFolders.Add(new ChatFolder
            {
                FolderId = folderId,
                UserId = userId,
                Name = "Favorites",
                SortOrder = 0,
                CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using var firstContext = _database.CreateContext();
        await using var secondContext = _database.CreateContext();
        var firstService = new ChatFolderService(firstContext);
        var secondService = new ChatFolderService(secondContext);

        var results = await Task.WhenAll(
            firstService.AddChatAsync(userId, folderId, chatId, CancellationToken.None),
            secondService.AddChatAsync(userId, folderId, chatId, CancellationToken.None));

        Assert.All(results, result => Assert.True(result));

        await using var verify = _database.CreateContext();
        Assert.Equal(1, await verify.ChatFolderChats.CountAsync(
            item => item.FolderId == folderId && item.ChatId == chatId));
    }



    private static User CreateUser(Guid userId) => new()
    {
        UserId = userId,
        FirstName = "Concurrency",
        LastName = "Test",
        RegistrationDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Account = new AccountSetting { AccountId = userId }
    };
}
