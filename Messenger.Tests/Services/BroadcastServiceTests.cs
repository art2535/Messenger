using FluentAssertions;
using Messenger.Core.DTOs.Broadcasts;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Messenger.Infrastructure.Services;
using Moq;
using Moq.EntityFrameworkCore;

namespace Messenger.Tests.Services
{
    public class BroadcastServiceTests
    {
        private readonly Mock<GuapMessengerContext> _contextMock;
        private readonly BroadcastService _service;

        public BroadcastServiceTests()
        {
            _contextMock = new Mock<GuapMessengerContext>();

            _contextMock.Setup(c => c.Users).ReturnsDbSet(new List<User>());
            _contextMock.Setup(c => c.Broadcasts).ReturnsDbSet(new List<Broadcast>());
            _contextMock.Setup(c => c.BroadcastRecipients).ReturnsDbSet(new List<BroadcastRecipient>());

            _contextMock.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(1);

            _contextMock.Setup(c => c.Broadcasts.Add(It.IsAny<Broadcast>()))
                .Callback<Broadcast>(b => b.BroadcastId = Guid.NewGuid());

            _service = new BroadcastService(_contextMock.Object);
        }

        #region CreateBroadcastAsync

        [Fact]
        public async Task CreateBroadcastAsync_AllRecipientsExist_ShouldCreateBroadcastSuccessfully()
        {
            var recipientIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
            var request = new CreateBroadcastRequest
            {
                Title = "Тестовая рассылка",
                MessageText = "Привет всем!",
                RecipientIds = recipientIds
            };
            var senderId = Guid.NewGuid();

            _contextMock.Setup(c => c.Users)
                        .ReturnsDbSet(recipientIds.Select(id => new User { UserId = id }).ToList());

            var result = await _service.CreateBroadcastAsync(request, senderId);

            result.Should().NotBeNull();
            result.TotalRecipients.Should().Be(2);
            result.BroadcastId.Should().NotBeEmpty();

            _contextMock.Verify(c => c.Broadcasts.Add(It.IsAny<Broadcast>()), Times.Once());
            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        }

        [Fact]
        public async Task CreateBroadcastAsync_SomeRecipientsDoNotExist_ShouldThrowArgumentException()
        {
            var recipientIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
            var request = new CreateBroadcastRequest
            {
                Title = "Рассылка",
                MessageText = "Текст",
                RecipientIds = recipientIds
            };

            _contextMock.Setup(c => c.Users)
                        .ReturnsDbSet(new List<User> { new User { UserId = recipientIds[0] } });

            await FluentActions.Invoking(() => _service.CreateBroadcastAsync(request, Guid.NewGuid()))
                .Should().ThrowAsync<ArgumentException>()
                .WithMessage("Один или несколько получателей не существуют");
        }

        #endregion

        #region MarkAsReadAsync

        [Fact]
        public async Task MarkAsReadAsync_RecipientNotFound_ShouldThrowKeyNotFoundException()
        {
            _contextMock.Setup(c => c.BroadcastRecipients).ReturnsDbSet(new List<BroadcastRecipient>());

            await FluentActions.Invoking(() => _service.MarkAsReadAsync(Guid.NewGuid(), Guid.NewGuid()))
                .Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage("Вы не являетесь получателем этой рассылки");
        }

        [Fact]
        public async Task MarkAsReadAsync_AlreadyRead_ShouldReturnSuccessWithoutUpdating()
        {
            var broadcastId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var readAt = DateTime.UtcNow.AddMinutes(-5);

            var recipient = new BroadcastRecipient
            {
                BroadcastId = broadcastId,
                UserId = userId,
                IsRead = true,
                ReadAt = readAt
            };

            _contextMock.Setup(c => c.BroadcastRecipients)
                        .ReturnsDbSet(new List<BroadcastRecipient> { recipient });

            var result = await _service.MarkAsReadAsync(broadcastId, userId);

            result.Success.Should().BeTrue();
            result.ReadAt.Should().Be(readAt);

            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
        }

        [Fact]
        public async Task MarkAsReadAsync_NotYetRead_ShouldMarkAsReadAndSave()
        {
            var broadcastId = Guid.NewGuid();
            var userId = Guid.NewGuid();

            var recipient = new BroadcastRecipient
            {
                BroadcastId = broadcastId,
                UserId = userId,
                IsRead = false
            };

            _contextMock.Setup(c => c.BroadcastRecipients)
                        .ReturnsDbSet(new List<BroadcastRecipient> { recipient });

            var result = await _service.MarkAsReadAsync(broadcastId, userId);

            result.Success.Should().BeTrue();
            result.ReadAt.Should().NotBeNull();

            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
            recipient.IsRead.Should().BeTrue();
        }

        #endregion

        #region GetMyBroadcastsAsync

        [Fact]
        public async Task GetMyBroadcastsAsync_ShouldReturnBroadcastsWithReadStatus()
        {
            var userId = Guid.NewGuid();

            var broadcast1 = new Broadcast
            {
                BroadcastId = Guid.NewGuid(),
                Title = "Рассылка 1",
                MessageText = "Текст 1",
                CreatedAt = DateTime.UtcNow.AddMinutes(-10)
            };

            var broadcast2 = new Broadcast
            {
                BroadcastId = Guid.NewGuid(),
                Title = "Рассылка 2",
                MessageText = "Текст 2",
                CreatedAt = DateTime.UtcNow
            };

            var items = new List<BroadcastRecipient>
            {
                new BroadcastRecipient
                {
                    BroadcastId = broadcast1.BroadcastId,
                    UserId = userId,
                    Broadcast = broadcast1,
                    IsRead = true,
                    ReadAt = DateTime.UtcNow
                },
                new BroadcastRecipient
                {
                    BroadcastId = broadcast2.BroadcastId,
                    UserId = userId,
                    Broadcast = broadcast2,
                    IsRead = false
                }
            };

            _contextMock.Setup(c => c.BroadcastRecipients)
                        .ReturnsDbSet(items);

            var result = await _service.GetMyBroadcastsAsync(userId, unreadOnly: false);

            result.Should().HaveCount(2);
        }

        #endregion
    }
}