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
        await SeedChatAsync(chatId, ownerId, memberId);
        await SeedUserAsync(newUserId);

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
        new(context, CreateEncryptionService(), new NoOpCacheService(), NullLogger<ChatService>.Instance);

    private MessageService CreateMessageService(Messenger.Infrastructure.Data.GuapMessengerContext context) =>
        new(context, CreateEncryptionService(), Mock.Of<MassTransit.IPublishEndpoint>(),
            new NoOpCacheService(), NullLogger<MessageService>.Instance);

    private sealed class NoOpCacheService : Messenger.Application.Interfaces.ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult<T?>(default);

        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(IEnumerable<string> keys, CancellationToken ct = default) =>
            Task.CompletedTask;
    }


    [Fact]
    public async Task MessageService_UpdateAndDeleteMessage_RequireOriginalSender()
    {
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        await SeedChatAsync(chatId, ownerId, memberId);
        await SeedMessagesAsync(chatId, CreateMessage(messageId, chatId, ownerId, 1, "before"));

        await using (var context = _database.CreateContext())
        {
            var service = CreateMessageService(context);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.UpdateMessageContentAsync(messageId, memberId, "not-owner"));
            await service.UpdateMessageContentAsync(messageId, ownerId, "after");
        }

        await using (var verify = _database.CreateContext())
            Assert.Equal("after", (await verify.Messages.SingleAsync(x => x.MessageId == messageId)).MessageText);

        await using (var context = _database.CreateContext())
        {
            var service = CreateMessageService(context);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.DeleteMessagesAsync(new[] { messageId }, chatId, memberId));
            Assert.Equal(1, await service.DeleteMessagesAsync(new[] { messageId, messageId }, chatId, ownerId));
        }

        await using var finalVerify = _database.CreateContext();
        Assert.False(await finalVerify.Messages.AnyAsync(x => x.MessageId == messageId));
    }

    [Fact]
    public async Task MessageService_UpdateDeliveryStatusAsync_DoesNotRegressDeliveredOrReadMessagesToFailed()
    {
        var ownerId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var pendingId = Guid.NewGuid();
        var deliveredId = Guid.NewGuid();
        var readId = Guid.NewGuid();
        await SeedChatAsync(chatId, ownerId);
        await SeedMessagesAsync(chatId,
            CreateMessage(pendingId, chatId, ownerId, 1),
            CreateMessage(deliveredId, chatId, ownerId, 2),
            CreateMessage(readId, chatId, ownerId, 3));

        await using (var seed = _database.CreateContext())
        {
            await seed.Messages.Where(x => x.MessageId == deliveredId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.DeliveryStatus, MessageDeliveryStatus.Delivered));
            await seed.Messages.Where(x => x.MessageId == readId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.DeliveryStatus, MessageDeliveryStatus.Read));
        }

        await using (var context = _database.CreateContext())
        {
            var service = CreateMessageService(context);
            await service.UpdateDeliveryStatusAsync(pendingId, MessageDeliveryStatus.Failed);
            await service.UpdateDeliveryStatusAsync(deliveredId, MessageDeliveryStatus.Failed);
            await service.UpdateDeliveryStatusAsync(readId, MessageDeliveryStatus.Failed);
        }

        await using var verify = _database.CreateContext();
        Assert.Equal(MessageDeliveryStatus.Failed,
            (await verify.Messages.SingleAsync(x => x.MessageId == pendingId)).DeliveryStatus);
        Assert.Equal(MessageDeliveryStatus.Delivered,
            (await verify.Messages.SingleAsync(x => x.MessageId == deliveredId)).DeliveryStatus);
        Assert.Equal(MessageDeliveryStatus.Read,
            (await verify.Messages.SingleAsync(x => x.MessageId == readId)).DeliveryStatus);
    }

    [Fact]
    public async Task MessageService_PagedHistory_EnforcesMembershipAndUsesExclusiveSequenceCursor()
    {
        var ownerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        await SeedChatAsync(chatId, ownerId);
        await SeedMessagesAsync(chatId,
            CreateMessage(Guid.NewGuid(), chatId, ownerId, 1),
            CreateMessage(Guid.NewGuid(), chatId, ownerId, 2),
            CreateMessage(Guid.NewGuid(), chatId, ownerId, 3));

        await using var context = _database.CreateContext();
        var service = CreateMessageService(context);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetMessagesPagedAsync(chatId, strangerId, 4, 2, CancellationToken.None));

        var firstPage = await service.GetMessagesPagedAsync(chatId, ownerId, 4, 2, CancellationToken.None);
        Assert.True(firstPage.HasMore);
        Assert.Equal(new long[] { 2, 3 }, firstPage.Items.Select(x => x.SequenceNumber));

        var secondPage = await service.GetMessagesPagedAsync(chatId, ownerId, 2, 2, CancellationToken.None);
        Assert.False(secondPage.HasMore);
        Assert.Single(secondPage.Items);
        Assert.Equal(1, secondPage.Items[0].SequenceNumber);
    }

    [Fact]
    public async Task MessageService_SearchPinAndExport_ReturnAuthorizedData()
    {
        var ownerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        await SeedChatAsync(chatId, ownerId);
        var encrypted = CreateEncryptionService().Encrypt("searchable phrase");
        await SeedMessagesAsync(chatId, CreateMessage(messageId, chatId, ownerId, 1, encrypted));

        await using var context = _database.CreateContext();
        var service = CreateMessageService(context);
        var results = await service.SearchMessagesAsync(chatId, ownerId, "searchable", CancellationToken.None);
        Assert.Single(results);
        Assert.Equal("searchable phrase", results[0].MessageText);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ExportChatAsync(chatId, strangerId, "json", token: CancellationToken.None));

        var pinned = await service.PinMessageAsync(messageId, ownerId);
        Assert.NotNull(pinned);
        Assert.True(pinned.IsPinned);
        var unpinned = await service.UnpinMessageAsync(messageId, ownerId);
        Assert.NotNull(unpinned);
        Assert.False(unpinned.IsPinned);

        var export = await service.ExportChatAsync(chatId, ownerId, "json", "safe/chat", CancellationToken.None);
        Assert.Equal("json", export.Format);
        Assert.Equal(1, export.MessageCount);
        Assert.Contains("searchable phrase", System.Text.Encoding.UTF8.GetString(export.Content));
    }

    private async Task SeedMessagesAsync(Guid chatId, params Message[] messages)
    {
        await using var context = _database.CreateContext();
        context.Messages.AddRange(messages);
        await context.SaveChangesAsync();
    }

    private async Task SeedUserAsync(Guid userId)
    {
        await using var context = _database.CreateContext();
        context.Users.Add(CreateUser(userId));
        await context.SaveChangesAsync();
    }

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
