using FluentAssertions;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Messenger.Infrastructure.Services;
using Moq;
using Moq.EntityFrameworkCore;

namespace Messenger.Tests.Services
{
    public class UserStatusServiceTests
    {
        private readonly Mock<GuapMessengerContext> _contextMock;
        private readonly UserStatusService _service;

        public UserStatusServiceTests()
        {
            _contextMock = new Mock<GuapMessengerContext>();
            _contextMock.Setup(c => c.UserStatuses).ReturnsDbSet(new List<UserStatus>());
            _contextMock.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _service = new UserStatusService(_contextMock.Object);
        }

        [Fact]
        public async Task GetUserStatusByUserIdAsync_WhenExists_ShouldReturnStatus()
        {
            var userId = Guid.NewGuid();
            var status = new UserStatus { UserId = userId, Online = true, LastActivity = DateTime.UtcNow };
            _contextMock.Setup(c => c.UserStatuses).ReturnsDbSet(new List<UserStatus> { status });

            var result = await _service.GetUserStatusByUserIdAsync(userId);

            result.Should().NotBeNull();
            result!.UserId.Should().Be(userId);
            result.Online.Should().BeTrue();
        }

        [Fact]
        public async Task GetUserStatusByUserIdAsync_WhenMissing_ShouldReturnNull()
        {
            _contextMock.Setup(c => c.UserStatuses).ReturnsDbSet(new List<UserStatus>());

            var result = await _service.GetUserStatusByUserIdAsync(Guid.NewGuid());

            result.Should().BeNull();
        }

        [Fact]
        public async Task GetInactiveOnlineStatusesAsync_ShouldFilterByOnlineAndActivity()
        {
            var olderThan = DateTime.UtcNow.AddMinutes(-5);
            var statuses = new List<UserStatus>
            {
                new UserStatus { UserId = Guid.NewGuid(), Online = true, LastActivity = olderThan.AddMinutes(-10) },
                new UserStatus { UserId = Guid.NewGuid(), Online = true, LastActivity = olderThan.AddMinutes(1) },
                new UserStatus { UserId = Guid.NewGuid(), Online = false, LastActivity = olderThan.AddMinutes(-20) },
                new UserStatus { UserId = Guid.NewGuid(), Online = true, LastActivity = null }
            };

            _contextMock.Setup(c => c.UserStatuses).ReturnsDbSet(statuses);

            var result = await _service.GetInactiveOnlineStatusesAsync(olderThan);

            result.Should().HaveCount(1);
            result[0].Online.Should().BeTrue();
            result[0].LastActivity.Should().BeBefore(olderThan);
        }

        [Fact]
        public async Task SetOfflineBatchAsync_EmptyIds_ShouldReturnEarly()
        {
            await _service.SetOfflineBatchAsync(Array.Empty<Guid>());
            await _service.SetOfflineBatchAsync(new List<Guid>());

            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
