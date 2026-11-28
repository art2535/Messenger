using Messenger.Core.Enums;
using Messenger.Core.Models;
using Messenger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

using Messenger.Tests.Shared.Fixtures;

namespace Messenger.Tests.Infrastructure.Chats.Reactions;

[Collection(PostgreSqlCollection.Name)]
public sealed class ReactionServiceTests
{
    private readonly PostgreSqlFixture _database;

    public ReactionServiceTests(PostgreSqlFixture database) => _database = database;



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
