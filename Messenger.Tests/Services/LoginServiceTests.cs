using FluentAssertions;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Messenger.Infrastructure.Services;
using Moq;
using Moq.EntityFrameworkCore;

namespace Messenger.Tests.Services
{
    public class LoginServiceTests
    {
        private readonly Mock<GuapMessengerContext> _contextMock;
        private readonly LoginService _service;

        public LoginServiceTests()
        {
            _contextMock = new Mock<GuapMessengerContext>();
            _contextMock.Setup(c => c.Logins).ReturnsDbSet(new List<Login>());
            _contextMock.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _service = new LoginService(_contextMock.Object);
        }

        [Fact]
        public async Task AddLoginAsync_ShouldAddAndSave()
        {
            var login = new Login
            {
                LoginId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                Token = "access-token",
                IpAddress = "127.0.0.1",
                LoginTime = DateTime.UtcNow,
                Active = true
            };

            var mockSet = new Mock<Microsoft.EntityFrameworkCore.DbSet<Login>>();
            _contextMock.Setup(c => c.Logins).Returns(mockSet.Object);

            await _service.AddLoginAsync(login);

            mockSet.Verify(s => s.AddAsync(login, It.IsAny<CancellationToken>()), Times.Once);
            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetLoginsByUserIdAsync_ShouldReturnUserLogins()
        {
            var userId = Guid.NewGuid();
            var otherId = Guid.NewGuid();
            var logins = new List<Login>
            {
                new Login { LoginId = Guid.NewGuid(), UserId = userId, Token = "t1", IpAddress = "1.1.1.1", Active = true },
                new Login { LoginId = Guid.NewGuid(), UserId = userId, Token = "t2", IpAddress = "1.1.1.2", Active = false },
                new Login { LoginId = Guid.NewGuid(), UserId = otherId, Token = "t3", IpAddress = "2.2.2.2", Active = true }
            };

            _contextMock.Setup(c => c.Logins).ReturnsDbSet(logins);

            var result = (await _service.GetLoginsByUserIdAsync(userId)).ToList();

            result.Should().HaveCount(2);
            result.Should().OnlyContain(l => l.UserId == userId);
        }

        [Fact]
        public async Task GetLoginsByUserIdAsync_WhenNone_ShouldReturnEmpty()
        {
            _contextMock.Setup(c => c.Logins).ReturnsDbSet(new List<Login>());

            var result = await _service.GetLoginsByUserIdAsync(Guid.NewGuid());

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task UpdateLoginAsync_ShouldUpdateAndSave()
        {
            var login = new Login
            {
                LoginId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                Token = "token",
                IpAddress = "127.0.0.1",
                Active = false,
                LogoutTime = DateTime.UtcNow
            };

            await _service.UpdateLoginAsync(login);

            _contextMock.Verify(c => c.Logins.Update(login), Times.Once);
            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CloseActiveLoginsForUsersAsync_EmptyIds_ShouldReturnWithoutQuery()
        {
            await _service.CloseActiveLoginsForUsersAsync(Array.Empty<Guid>());
            await _service.CloseActiveLoginsForUsersAsync(new List<Guid>());

            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
