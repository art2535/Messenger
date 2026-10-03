using FluentAssertions;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Messenger.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.EntityFrameworkCore;

namespace Messenger.Tests.Services
{
    public class ChatServiceTests
    {
        private readonly Mock<GuapMessengerContext> _contextMock;
        private readonly Mock<IUserService> _userServiceMock;
        private readonly Mock<IEncryptionService> _encryptionMock;
        private readonly Mock<ICacheService> _cacheMock;
        private readonly Mock<ILogger<ChatService>> _loggerMock;
        private readonly ChatService _service;

        public ChatServiceTests()
        {
            _contextMock = new Mock<GuapMessengerContext>();
            _userServiceMock = new Mock<IUserService>();
            _encryptionMock = new Mock<IEncryptionService>();
            _cacheMock = new Mock<ICacheService>();
            _loggerMock = new Mock<ILogger<ChatService>>();

            _contextMock.Setup(c => c.Chats).ReturnsDbSet(new List<Chat>());
            _contextMock.Setup(c => c.ChatParticipants).ReturnsDbSet(new List<ChatParticipant>());
            _contextMock.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            _cacheMock.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _cacheMock.Setup(c => c.RemoveAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _cacheMock.Setup(c => c.GetAsync<List<object>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((List<object>?)null);

            _service = new ChatService(
                _contextMock.Object,
                _userServiceMock.Object,
                _encryptionMock.Object,
                _cacheMock.Object,
                _loggerMock.Object);
        }

        [Fact]
        public async Task CreateChatAsync_ShouldCreateChatAndOwnerParticipant_AndInvalidateCache()
        {
            var creatorId = Guid.NewGuid();
            var chats = new List<Chat>();
            var participants = new List<ChatParticipant>();

            var chatsSet = new Mock<Microsoft.EntityFrameworkCore.DbSet<Chat>>();
            chatsSet.Setup(s => s.AddAsync(It.IsAny<Chat>(), It.IsAny<CancellationToken>()))
                .Callback<Chat, CancellationToken>((c, _) => chats.Add(c))
                .ReturnsAsync((Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<Chat>?)null!);
            _contextMock.Setup(c => c.Chats).Returns(chatsSet.Object);

            var partsSet = new Mock<Microsoft.EntityFrameworkCore.DbSet<ChatParticipant>>();
            partsSet.Setup(s => s.AddAsync(It.IsAny<ChatParticipant>(), It.IsAny<CancellationToken>()))
                .Callback<ChatParticipant, CancellationToken>((p, _) => participants.Add(p))
                .ReturnsAsync((Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<ChatParticipant>?)null!);
            _contextMock.Setup(c => c.ChatParticipants).Returns(partsSet.Object);

            var chat = await _service.CreateChatAsync("Team", "group", creatorId);

            chat.Should().NotBeNull();
            chat.Name.Should().Be("Team");
            chat.Type.Should().Be("group");
            chat.UserId.Should().Be(creatorId);
            chat.ChatId.Should().NotBe(Guid.Empty);

            chatsSet.Verify(s => s.AddAsync(It.IsAny<Chat>(), It.IsAny<CancellationToken>()), Times.Once);
            partsSet.Verify(s => s.AddAsync(
                It.Is<ChatParticipant>(p => p.UserId == creatorId && p.Role == "владелец"),
                It.IsAny<CancellationToken>()), Times.Once);
            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
            _cacheMock.Verify(c => c.RemoveAsync(It.Is<string>(k => k.Contains(creatorId.ToString("N"))), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetChatByIdAsync_WhenExists_ShouldReturnChat()
        {
            var chatId = Guid.NewGuid();
            var chat = new Chat
            {
                ChatId = chatId,
                Name = "DM",
                Type = "private",
                UserId = Guid.NewGuid(),
                CreationDate = DateTime.UtcNow,
                ChatParticipants = new List<ChatParticipant>()
            };

            _contextMock.Setup(c => c.Chats).ReturnsDbSet(new List<Chat> { chat });

            var result = await _service.GetChatByIdAsync(chatId);

            result.Should().NotBeNull();
            result!.ChatId.Should().Be(chatId);
            result.Name.Should().Be("DM");
        }

        [Fact]
        public async Task GetChatByIdAsync_WhenMissing_ShouldReturnNull()
        {
            _contextMock.Setup(c => c.Chats).ReturnsDbSet(new List<Chat>());

            var result = await _service.GetChatByIdAsync(Guid.NewGuid());

            result.Should().BeNull();
        }

        [Fact]
        public async Task GetChatParticipantsAsync_ShouldReturnParticipantsForChat()
        {
            var chatId = Guid.NewGuid();
            var otherChat = Guid.NewGuid();
            var parts = new List<ChatParticipant>
            {
                new ChatParticipant { ChatId = chatId, UserId = Guid.NewGuid(), Role = "владелец", JoinDate = DateTime.UtcNow },
                new ChatParticipant { ChatId = chatId, UserId = Guid.NewGuid(), Role = "участник", JoinDate = DateTime.UtcNow },
                new ChatParticipant { ChatId = otherChat, UserId = Guid.NewGuid(), Role = "владелец", JoinDate = DateTime.UtcNow }
            };

            _contextMock.Setup(c => c.ChatParticipants).ReturnsDbSet(parts);

            var result = (await _service.GetChatParticipantsAsync(chatId)).ToList();

            result.Should().HaveCount(2);
            result.Should().OnlyContain(p => p.ChatId == chatId);
        }

        [Fact]
        public async Task UpdateChatAsync_ShouldUpdateAndInvalidateCache()
        {
            var chatId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var chat = new Chat { ChatId = chatId, Name = "New Name", Type = "group", UserId = userId };

            _contextMock.Setup(c => c.ChatParticipants).ReturnsDbSet(new List<ChatParticipant>
            {
                new ChatParticipant { ChatId = chatId, UserId = userId, Role = "владелец" }
            });

            await _service.UpdateChatAsync(chat);

            _contextMock.Verify(c => c.Chats.Update(chat), Times.Once);
            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
            _cacheMock.Verify(c => c.RemoveAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetUserChatsWithLastMessageAsync_WhenCacheHit_ShouldReturnCached()
        {
            var userId = Guid.NewGuid();
            var cached = new List<object> { new { ChatId = Guid.NewGuid(), Name = "Cached" } };

            _cacheMock.Setup(c => c.GetAsync<List<object>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(cached);

            var result = await _service.GetUserChatsWithLastMessageAsync(userId);

            result.Should().BeSameAs(cached);
        }

        [Fact]
        public async Task DeleteParticipantFromChatAsync_WhenExists_ShouldRemoveAndInvalidate()
        {
            var chatId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var part = new ChatParticipant { ChatId = chatId, UserId = userId, Role = "участник" };

            _contextMock.Setup(c => c.ChatParticipants).ReturnsDbSet(new List<ChatParticipant> { part });

            await _service.DeleteParticipantFromChatAsync(chatId, userId);

            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
            _cacheMock.Verify(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task DeleteChatAsync_ShouldRemoveAndClearParticipantsCache()
        {
            var chatId = Guid.NewGuid();
            var userA = Guid.NewGuid();
            var userB = Guid.NewGuid();
            var chat = new Chat { ChatId = chatId, Name = "to-delete", Type = "group", UserId = userA };

            _contextMock.Setup(c => c.ChatParticipants).ReturnsDbSet(new List<ChatParticipant>
            {
                new ChatParticipant { ChatId = chatId, UserId = userA },
                new ChatParticipant { ChatId = chatId, UserId = userB }
            });

            await _service.DeleteChatAsync(chat);

            _contextMock.Verify(c => c.Chats.Remove(chat), Times.Once);
            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
            _cacheMock.Verify(c => c.RemoveAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
