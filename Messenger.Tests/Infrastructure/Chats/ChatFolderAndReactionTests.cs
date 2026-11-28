using Messenger.Core.Enums;
using Messenger.Core.Models;
using Messenger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

using Messenger.Tests.Shared.Fixtures;

namespace Messenger.Tests.Infrastructure.Chats;

[Collection(PostgreSqlCollection.Name)]
public sealed class ChatFolderAndReactionTests
{
    private readonly PostgreSqlFixture _database;

    public ChatFolderAndReactionTests(PostgreSqlFixture database) => _database = database;

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

    [Fact]
    public async Task AddReactionAsync_ConcurrentCalls_KeepOneReactionPerUserAndMessage()
    {
        var userId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await using (var seed = _database.CreateContext())
        {
            seed.Users.Add(CreateUser(userId));
            seed.Chats.Add(new Chat
            {
                ChatId = chatId,
                Name = "Reaction concurrency",
                Type = "private",
                UserId = userId,
                CreationDate = DateTime.UtcNow
            });
            seed.Messages.Add(new Message
            {
                MessageId = messageId,
                ChatId = chatId,
                SenderId = userId,
                MessageText = "React to this",
                SendTime = DateTime.UtcNow,
                SequenceNumber = 1,
                DeliveryStatus = MessageDeliveryStatus.Sent
            });
            await seed.SaveChangesAsync();
        }

        await using var firstContext = _database.CreateContext();
        await using var secondContext = _database.CreateContext();
        var firstService = new ReactionService(firstContext);
        var secondService = new ReactionService(secondContext);

        var results = await Task.WhenAll(
            firstService.AddReactionAsync(messageId, userId, "👍", CancellationToken.None),
            secondService.AddReactionAsync(messageId, userId, "❤️", CancellationToken.None));

        Assert.All(results, result =>
        {
            Assert.Equal(messageId, result.MessageId);
            Assert.Equal(userId, result.UserId);
        });

        await using var verify = _database.CreateContext();
        var reactions = await verify.Reactions
            .Where(item => item.MessageId == messageId && item.UserId == userId)
            .ToListAsync();

        var reaction = Assert.Single(reactions);
        Assert.Contains(reaction.ReactionType, new[] { "👍", "❤️" });
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
