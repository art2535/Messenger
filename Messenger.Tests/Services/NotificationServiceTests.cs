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
    public class NotificationServiceTests
    {
        private readonly Mock<GuapMessengerContext> _contextMock;
        private readonly Mock<IEncryptionService> _encryptionMock;
        private readonly Mock<ILogger<NotificationService>> _loggerMock;
        private readonly NotificationService _service;

        public NotificationServiceTests()
        {
            _contextMock = new Mock<GuapMessengerContext>();
            _encryptionMock = new Mock<IEncryptionService>();
            _loggerMock = new Mock<ILogger<NotificationService>>();

            _contextMock.Setup(c => c.Notifications).ReturnsDbSet(new List<Notification>());
            _contextMock.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            _encryptionMock.Setup(e => e.Encrypt(It.IsAny<string>()))
                .Returns<string>(t => $"enc:{t}");
            _encryptionMock.Setup(e => e.TryDecryptSafe(It.IsAny<string?>()))
                .Returns<string?>(t => t != null && t.StartsWith("enc:") ? t[4..] : "[broken]");

            _service = new NotificationService(_contextMock.Object, _encryptionMock.Object, _loggerMock.Object);
        }

        [Fact]
        public async Task CreateNotificationAsync_ShouldEncryptAndPersist()
        {
            var userId = Guid.NewGuid();
            var text = "Новое сообщение";

            var mockSet = new Mock<Microsoft.EntityFrameworkCore.DbSet<Notification>>();
            _contextMock.Setup(c => c.Notifications).Returns(mockSet.Object);

            var id = await _service.CreateNotificationAsync(userId, text);

            id.Should().NotBe(Guid.Empty);
            _encryptionMock.Verify(e => e.Encrypt(text), Times.Once);
            mockSet.Verify(s => s.AddAsync(
                It.Is<Notification>(n => n.UserId == userId && n.Text == "enc:Новое сообщение" && !n.Read),
                It.IsAny<CancellationToken>()), Times.Once);
            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetNotificationsAsync_ShouldDecryptAndOrderDesc()
        {
            var userId = Guid.NewGuid();
            var list = new List<Notification>
            {
                new Notification
                {
                    NotificationId = Guid.NewGuid(),
                    UserId = userId,
                    Text = "enc:old",
                    CreationDate = DateTime.UtcNow.AddHours(-2),
                    Read = true
                },
                new Notification
                {
                    NotificationId = Guid.NewGuid(),
                    UserId = userId,
                    Text = "enc:new",
                    CreationDate = DateTime.UtcNow,
                    Read = false
                },
                new Notification
                {
                    NotificationId = Guid.NewGuid(),
                    UserId = Guid.NewGuid(),
                    Text = "enc:other",
                    CreationDate = DateTime.UtcNow,
                    Read = false
                }
            };

            _contextMock.Setup(c => c.Notifications).ReturnsDbSet(list);

            var result = (await _service.GetNotificationsAsync(userId)).ToList();

            result.Should().HaveCount(2);
            result[0].Text.Should().Be("new");
            result[1].Text.Should().Be("old");
            _encryptionMock.Verify(e => e.TryDecryptSafe(It.IsAny<string?>()), Times.Exactly(2));
        }

        [Fact]
        public async Task GetNotificationsAsync_WhenEmpty_ShouldReturnEmpty()
        {
            _contextMock.Setup(c => c.Notifications).ReturnsDbSet(new List<Notification>());

            var result = await _service.GetNotificationsAsync(Guid.NewGuid());

            result.Should().BeEmpty();
        }
    }
}
