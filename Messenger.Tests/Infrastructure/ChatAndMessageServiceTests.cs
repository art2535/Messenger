using Messenger.Core.Enums;
using Messenger.Core.Models;
using Messenger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Messenger.Tests.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class ChatAndMessageServiceTests
{
    private readonly PostgreSqlFixture _database;

    public ChatAndMessageServiceTests(PostgreSqlFixture database) => _database = database;

    [Fact]
    public async Task ChatService_GetChatByIdAsync_EnforcesMembershipForActorAwareOverload()
    {
        var ownerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        await SeedChatAsync(chatId, ownerId);

        await using var context = _database.CreateContext();
        var service = CreateChatService(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetChatByIdAsync(chatId, strangerId, CancellationToken.None));

        var chat = await service.GetChatByIdAsync(chatId, ownerId, CancellationToken.None);
        Assert.NotNull(chat);
        Assert.Equal(chatId, chat.ChatId);
        Assert.Single(chat.Participants);
    }

    [Fact]
    public async Task ChatService_AddParticipantAsync_RequiresAdministratorAndOnlyAllowsRegularRole()
    {
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var newUserId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        await SeedChatAsync(chatId, ownerId, memberId, newUserId);

        await using var context = _database.CreateContext();
        var service = CreateChatService(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.AddParticipantToChatAsync(chatId, memberId, newUserId, "участник", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddParticipantToChatAsync(chatId, ownerId, newUserId, "admin", CancellationToken.None));

        await service.AddParticipantToChatAsync(chatId, ownerId, newUserId, "участник", CancellationToken.None);

        Assert.True(await context.ChatParticipants.AnyAsync(x => x.ChatId == chatId && x.UserId == newUserId));
    }

    [Fact]
    public async Task ChatService_DeleteChatAsync_OnlyAllowsActualOwner()
    {
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        await SeedChatAsync(chatId, ownerId, memberId);

        await using (var context = _database.CreateContext())
        {
            var service = CreateChatService(context);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.DeleteChatAsync(chatId, memberId, CancellationToken.None));
        }

        await using (var context = _database.CreateContext())
        {
            var service = CreateChatService(context);
            await service.DeleteChatAsync(chatId, ownerId, CancellationToken.None);
        }

        await using var verify = _database.CreateContext();
        Assert.False(await verify.Chats.AnyAsync(x => x.ChatId == chatId));
        Assert.False(await verify.ChatParticipants.AnyAsync(x => x.ChatId == chatId));
    }

    [Fact]
    public async Task MessageService_MarkMessagesAsReadAsync_RequiresMembershipAndOnlyMarksIncomingMessages()
    {
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var incomingId = Guid.NewGuid();
        var ownId = Guid.NewGuid();
        await SeedChatAsync(chatId, ownerId, memberId);

        await using (var seed = _database.CreateContext())
        {
            seed.Messages.AddRange(
                CreateMessage(incomingId, chatId, ownerId, 1),
                CreateMessage(ownId, chatId, memberId, 2));
            await seed.SaveChangesAsync();
        }

        await using var context = _database.CreateContext();
        var service = CreateMessageService(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.MarkMessagesAsReadAsync(chatId, strangerId, CancellationToken.None));

        Assert.Equal(1, await service.MarkMessagesAsReadAsync(chatId, memberId, CancellationToken.None));

        await using var verify = _database.CreateContext();
        var incoming = await verify.Messages.SingleAsync(x => x.MessageId == incomingId);
        var own = await verify.Messages.SingleAsync(x => x.MessageId == ownId);
        Assert.NotNull(incoming.ReadTime);
        Assert.Equal(MessageDeliveryStatus.Read, incoming.DeliveryStatus);
        Assert.Null(own.ReadTime);
        Assert.NotEqual(MessageDeliveryStatus.Read, own.DeliveryStatus);
    }

    [Fact]
    public async Task MessageService_SearchAndPin_RejectNonParticipants()
    {
        var ownerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        await SeedChatAsync(chatId, ownerId);

        await using (var seed = _database.CreateContext())
        {
            seed.Messages.Add(CreateMessage(messageId, chatId, ownerId, 1, "encrypted-content"));
            await seed.SaveChangesAsync();
        }

        await using var context = _database.CreateContext();
        var service = CreateMessageService(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SearchMessagesAsync(chatId, strangerId, "secret", CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.PinMessageAsync(messageId, strangerId, CancellationToken.None));
    }

    private ChatService CreateChatService(Messenger.Infrastructure.Data.GuapMessengerContext context) =>
        new(context, CreateEncryptionService(),
            Mock.Of<Messenger.Application.Interfaces.ICacheService>(),
            NullLogger<ChatService>.Instance);

    private MessageService CreateMessageService(Messenger.Infrastructure.Data.GuapMessengerContext context) =>
        new(context, CreateEncryptionService(),
            Mock.Of<MassTransit.IPublishEndpoint>(),
            Mock.Of<Messenger.Application.Interfaces.ICacheService>(),
            NullLogger<MessageService>.Instance);

    private async Task SeedChatAsync(Guid chatId, Guid ownerId, params Guid[] otherUserIds)
    {
        await using var context = _database.CreateContext();
        var users = new[] { ownerId }.Concat(otherUserIds).Distinct().Select(CreateUser).ToList();
        context.Users.AddRange(users);
        context.Chats.Add(new Chat
        {
            ChatId = chatId,
            Name = "Service test",
            Type = "group",
            UserId = ownerId,
            CreationDate = DateTime.UtcNow
        });
        context.ChatParticipants.Add(new ChatParticipant
        {
            ChatId = chatId,
            UserId = ownerId,
            Role = "владелец",
            JoinDate = DateTime.UtcNow
        });
        foreach (var userId in otherUserIds.Distinct())
        {
            context.ChatParticipants.Add(new ChatParticipant
            {
                ChatId = chatId,
                UserId = userId,
                Role = "участник",
                JoinDate = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync();
    }

    private static User CreateUser(Guid userId) => new()
    {
        UserId = userId,
        FirstName = "Chat",
        LastName = "Test",
        RegistrationDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Account = new AccountSetting { AccountId = userId }
    };

    private static Message CreateMessage(Guid messageId, Guid chatId, Guid senderId, long sequence, string text = "message") => new()
    {
        MessageId = messageId,
        ChatId = chatId,
        SenderId = senderId,
        MessageText = text,
        SendTime = DateTime.UtcNow,
        SequenceNumber = sequence,
        DeliveryStatus = MessageDeliveryStatus.Sent
    };

    private static AesGcmEncryptionService CreateEncryptionService() =>
        new(Options.Create(new AesGcmEncryptionOptions
        {
            MasterKeyBase64 = Convert.ToBase64String(Enumerable.Range(0, 32).Select(x => (byte)x).ToArray())
        }));
}
