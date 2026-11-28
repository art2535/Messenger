using Messenger.Core.Enums;
using Messenger.Core.Models;
using Messenger.Infrastructure.Services;
using ChatServiceImplementation = Messenger.Infrastructure.Services.ChatService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

using Messenger.Tests.Shared.Fixtures;

using Messenger.Tests.Shared.TestDoubles;

namespace Messenger.Tests.Infrastructure.Chats.ChatService;

[Collection(PostgreSqlCollection.Name)]
public sealed class ChatServiceTests
{
    private readonly PostgreSqlFixture _database;

    public ChatServiceTests(PostgreSqlFixture database) => _database = database;

    [Fact]
    public async Task ChatService_GetUserChatsWithLastMessageAsync_RevalidatesCachedMembership()
    {
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        await SeedChatAsync(chatId, ownerId, memberId);

        await using (var context = _database.CreateContext())
        {
            context.ChatParticipants.RemoveRange(
                context.ChatParticipants.Where(participant =>
                    participant.ChatId == chatId && participant.UserId == memberId));
            await context.SaveChangesAsync();
        }

        var staleCache = new Mock<Messenger.Application.Interfaces.ICacheService>();
        staleCache
            .Setup(cache => cache.GetAsync<List<Messenger.Application.DTOs.Chats.ChatListItemDto>>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Messenger.Application.DTOs.Chats.ChatListItemDto>
            {
                new() { ChatId = chatId, Name = "Private chat", Type = "group" }
            });

        await using var readContext = _database.CreateContext();
        var service = new ChatServiceImplementation(
            readContext,
            CreateEncryptionService(),
            staleCache.Object,
            NullLogger<ChatServiceImplementation>.Instance);

        var chats = await service.GetUserChatsWithLastMessageAsync(memberId, CancellationToken.None);

        Assert.Empty(chats);
        staleCache.Verify(cache => cache.RemoveAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

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





    private ChatServiceImplementation CreateChatService(Messenger.Infrastructure.Data.GuapMessengerContext context) =>
        new(context, CreateEncryptionService(), new NoOpCacheService(), NullLogger<ChatServiceImplementation>.Instance);










    [Fact]
    public async Task ChatService_CreateChatAsync_CreatesOwnerMembershipAndPeerLookupIsScoped()
    {
        var ownerId = Guid.NewGuid();
        var peerId = Guid.NewGuid();
        await SeedUserAsync(ownerId);
        await SeedUserAsync(peerId);

        Guid chatId;
        await using (var context = _database.CreateContext())
        {
            var service = CreateChatService(context);
            var created = await service.CreateChatAsync("new chat", "group", ownerId);
            chatId = created.ChatId;
            Assert.Equal(ownerId, created.UserId);
        }

        await using (var seed = _database.CreateContext())
        {
            seed.ChatParticipants.Add(new ChatParticipant
            {
                ChatId = chatId,
                UserId = peerId,
                Role = "участник",
                JoinDate = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using var verify = _database.CreateContext();
        var chatService = CreateChatService(verify);
        var participants = await chatService.GetChatParticipantUserIdsAsync(chatId);
        Assert.Equal(2, participants.Count);
        Assert.Contains(ownerId, participants);
        Assert.Contains(peerId, participants);

        var peers = await chatService.GetPeerUserIdsAsync(ownerId);
        Assert.Contains(ownerId, peers);
        Assert.Contains(peerId, peers);
        Assert.DoesNotContain(Guid.NewGuid(), peers);
    }

    [Fact]
    public async Task ChatService_UpdateChatAsync_RequiresGroupAdministrator()
    {
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        await SeedChatAsync(chatId, ownerId, memberId);

        await using (var context = _database.CreateContext())
        {
            var service = CreateChatService(context);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.UpdateChatAsync(new Messenger.Application.DTOs.Chats.UpdateChatDto
                {
                    ChatId = chatId,
                    Name = "Renamed by member"
                }, memberId));
        }

        await using (var context = _database.CreateContext())
        {
            var service = CreateChatService(context);
            await service.UpdateChatAsync(new Messenger.Application.DTOs.Chats.UpdateChatDto
            {
                ChatId = chatId,
                Name = "Renamed by owner"
            }, ownerId);
        }

        await using var verify = _database.CreateContext();
        Assert.Equal("Renamed by owner", (await verify.Chats.SingleAsync(x => x.ChatId == chatId)).Name);
    }

    [Fact]
    public async Task ChatService_DeleteParticipantAsync_RejectsSelfRemovalAndRemovesRegularMember()
    {
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        await SeedChatAsync(chatId, ownerId, memberId);

        await using (var context = _database.CreateContext())
        {
            var service = CreateChatService(context);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.DeleteParticipantFromChatAsync(chatId, ownerId, ownerId));
            await service.DeleteParticipantFromChatAsync(chatId, ownerId, memberId);
        }

        await using var verify = _database.CreateContext();
        Assert.True(await verify.ChatParticipants.AnyAsync(x => x.ChatId == chatId && x.UserId == ownerId));
        Assert.False(await verify.ChatParticipants.AnyAsync(x => x.ChatId == chatId && x.UserId == memberId));
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


    private static AesGcmEncryptionService CreateEncryptionService() =>
        new(Options.Create(new AesGcmEncryptionOptions
        {
            MasterKeyBase64 = Convert.ToBase64String(Enumerable.Range(0, 32).Select(x => (byte)x).ToArray())
        }));
}
