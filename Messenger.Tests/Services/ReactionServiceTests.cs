using FluentAssertions;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Messenger.Infrastructure.Services;
using Moq;
using Moq.EntityFrameworkCore;

namespace Messenger.Tests.Services
{
    public class ReactionServiceTests
    {
        private readonly Mock<GuapMessengerContext> _contextMock;
        private readonly ReactionService _service;

        public ReactionServiceTests()
        {
            _contextMock = new Mock<GuapMessengerContext>();
            _contextMock.Setup(c => c.Reactions).ReturnsDbSet(new List<Reaction>());
            _contextMock.Setup(c => c.Messages).ReturnsDbSet(new List<Message>());
            _contextMock.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _service = new ReactionService(_contextMock.Object);
        }

        [Fact]
        public async Task AddReactionAsync_EmptyReactionType_ShouldThrow()
        {
            var reaction = new Reaction
            {
                MessageId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                ReactionType = "   "
            };

            var act = () => _service.AddReactionAsync(reaction);
            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*Тип реакции обязателен*");
        }

        [Fact]
        public async Task AddReactionAsync_TooLongReactionType_ShouldThrow()
        {
            var reaction = new Reaction
            {
                MessageId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                ReactionType = new string('x', 31)
            };

            var act = () => _service.AddReactionAsync(reaction);
            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*не должен превышать 30 символов*");
        }

        [Fact]
        public async Task AddReactionAsync_MessageNotFound_ShouldThrow()
        {
            var messageId = Guid.NewGuid();
            _contextMock.Setup(c => c.Messages).ReturnsDbSet(new List<Message>());

            var reaction = new Reaction
            {
                MessageId = messageId,
                UserId = Guid.NewGuid(),
                ReactionType = "👍"
            };

            var act = () => _service.AddReactionAsync(reaction);
            await act.Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage("*Сообщение не найдено*");
        }

        [Fact]
        public async Task AddReactionAsync_NewReaction_ShouldAddAndSave()
        {
            var messageId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            _contextMock.Setup(c => c.Messages).ReturnsDbSet(new List<Message>
            {
                new Message { MessageId = messageId, ChatId = Guid.NewGuid(), MessageText = "hi" }
            });
            _contextMock.Setup(c => c.Reactions).ReturnsDbSet(new List<Reaction>());

            var reaction = new Reaction
            {
                MessageId = messageId,
                UserId = userId,
                ReactionType = "❤️"
            };

            await _service.AddReactionAsync(reaction);

            reaction.ReactionId.Should().NotBe(Guid.Empty);
            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AddReactionAsync_ExistingReaction_ShouldUpdateType()
        {
            var messageId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var existingId = Guid.NewGuid();
            var existing = new Reaction
            {
                ReactionId = existingId,
                MessageId = messageId,
                UserId = userId,
                ReactionType = "👍"
            };

            _contextMock.Setup(c => c.Messages).ReturnsDbSet(new List<Message>
            {
                new Message { MessageId = messageId, ChatId = Guid.NewGuid(), MessageText = "hi" }
            });
            _contextMock.Setup(c => c.Reactions).ReturnsDbSet(new List<Reaction> { existing });

            var reaction = new Reaction
            {
                MessageId = messageId,
                UserId = userId,
                ReactionType = "🔥"
            };

            await _service.AddReactionAsync(reaction);

            existing.ReactionType.Should().Be("🔥");
            reaction.ReactionId.Should().Be(existingId);
            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetReactionsByMessageIdAsync_ShouldReturnOrdered()
        {
            var messageId = Guid.NewGuid();
            var reactions = new List<Reaction>
            {
                new Reaction { ReactionId = Guid.NewGuid(), MessageId = messageId, UserId = Guid.NewGuid(), ReactionType = "z" },
                new Reaction { ReactionId = Guid.NewGuid(), MessageId = messageId, UserId = Guid.NewGuid(), ReactionType = "a" },
                new Reaction { ReactionId = Guid.NewGuid(), MessageId = Guid.NewGuid(), UserId = Guid.NewGuid(), ReactionType = "x" }
            };

            _contextMock.Setup(c => c.Reactions).ReturnsDbSet(reactions);

            var result = (await _service.GetReactionsByMessageIdAsync(messageId)).ToList();

            result.Should().HaveCount(2);
            result.Select(r => r.ReactionType).Should().BeInAscendingOrder();
        }

        [Fact]
        public async Task DeleteReactionAsync_WhenExists_ShouldRemoveAndSave()
        {
            var messageId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var reaction = new Reaction
            {
                ReactionId = Guid.NewGuid(),
                MessageId = messageId,
                UserId = userId,
                ReactionType = "👍"
            };

            _contextMock.Setup(c => c.Reactions).ReturnsDbSet(new List<Reaction> { reaction });

            await _service.DeleteReactionAsync(messageId, userId);

            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteReactionAsync_WhenMissing_ShouldNotSave()
        {
            _contextMock.Setup(c => c.Reactions).ReturnsDbSet(new List<Reaction>());

            await _service.DeleteReactionAsync(Guid.NewGuid(), Guid.NewGuid());

            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task MessageExistsAsync_ShouldReturnTrueWhenPresent()
        {
            var messageId = Guid.NewGuid();
            _contextMock.Setup(c => c.Messages).ReturnsDbSet(new List<Message>
            {
                new Message { MessageId = messageId, MessageText = "test" }
            });

            (await _service.MessageExistsAsync(messageId)).Should().BeTrue();
            (await _service.MessageExistsAsync(Guid.NewGuid())).Should().BeFalse();
        }

        [Fact]
        public async Task GetChatIdByMessageIdAsync_ShouldReturnChatIdOrNull()
        {
            var messageId = Guid.NewGuid();
            var chatId = Guid.NewGuid();
            _contextMock.Setup(c => c.Messages).ReturnsDbSet(new List<Message>
            {
                new Message { MessageId = messageId, ChatId = chatId, MessageText = "x" }
            });

            (await _service.GetChatIdByMessageIdAsync(messageId)).Should().Be(chatId);
            (await _service.GetChatIdByMessageIdAsync(Guid.NewGuid())).Should().BeNull();
        }
    }
}
