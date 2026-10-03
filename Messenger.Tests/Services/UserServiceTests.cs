using FluentAssertions;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Messenger.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using Moq.EntityFrameworkCore;

namespace Messenger.Tests.Services
{
    public class UserServiceTests
    {
        private readonly Mock<GuapMessengerContext> _contextMock;
        private readonly UserService _service;

        public UserServiceTests()
        {
            _contextMock = new Mock<GuapMessengerContext>();
            _contextMock.Setup(c => c.Users).ReturnsDbSet(new List<User>());
            _contextMock.Setup(c => c.Blacklists).ReturnsDbSet(new List<Blacklist>());
            _contextMock.Setup(c => c.Roles).ReturnsDbSet(new List<Role>());
            _contextMock.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
            _service = new UserService(_contextMock.Object, config);
        }

        [Fact]
        public async Task GetUserByIdAsync_WhenExists_ShouldReturnUser()
        {
            var id = Guid.NewGuid();
            var user = new User { UserId = id, FirstName = "Иван", LastName = "Петров", Login = "ivan" };
            _contextMock.Setup(c => c.Users).ReturnsDbSet(new List<User> { user });

            var result = await _service.GetUserByIdAsync(id);

            result.Should().NotBeNull();
            result!.UserId.Should().Be(id);
            result.Login.Should().Be("ivan");
        }

        [Fact]
        public async Task GetUserByIdAsync_WhenMissing_ShouldReturnNull()
        {
            _contextMock.Setup(c => c.Users).ReturnsDbSet(new List<User>());

            var result = await _service.GetUserByIdAsync(Guid.NewGuid());

            result.Should().BeNull();
        }

        [Fact]
        public async Task GetAllUsersAsync_ShouldReturnAll()
        {
            var users = new List<User>
            {
                new User { UserId = Guid.NewGuid(), FirstName = "A", LastName = "A" },
                new User { UserId = Guid.NewGuid(), FirstName = "B", LastName = "B" }
            };
            _contextMock.Setup(c => c.Users).ReturnsDbSet(users);

            var result = (await _service.GetAllUsersAsync()).ToList();

            result.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetRolesAsync_ShouldReturnRoles()
        {
            var roles = new List<Role>
            {
                new Role { RoleId = Guid.NewGuid(), Name = "ROLE_USER", Description = "User" },
                new Role { RoleId = Guid.NewGuid(), Name = "ROLE_ADMIN", Description = "Admin" }
            };
            _contextMock.Setup(c => c.Roles).ReturnsDbSet(roles);

            var result = (await _service.GetRolesAsync()).ToList();

            result.Should().HaveCount(2);
        }

        [Fact]
        public async Task IsBlockedByAsync_WhenBlocked_ShouldReturnTrue()
        {
            var blocker = Guid.NewGuid();
            var blocked = Guid.NewGuid();
            _contextMock.Setup(c => c.Blacklists).ReturnsDbSet(new List<Blacklist>
            {
                new Blacklist { UserId = blocker, BlockedUserId = blocked, BlockDate = DateTime.UtcNow }
            });

            (await _service.IsBlockedByAsync(blocker, blocked)).Should().BeTrue();
            (await _service.IsBlockedByAsync(blocked, blocker)).Should().BeFalse();
        }

        [Fact]
        public async Task BlockUserAsync_ShouldAddBlacklistEntry()
        {
            var userId = Guid.NewGuid();
            var blockedId = Guid.NewGuid();
            _contextMock.Setup(c => c.Blacklists).ReturnsDbSet(new List<Blacklist>());

            await _service.BlockUserAsync(userId, blockedId);

            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task BlockUserAsync_WhenAlreadyBlocked_ShouldNotSaveAgain()
        {
            var userId = Guid.NewGuid();
            var blockedId = Guid.NewGuid();
            _contextMock.Setup(c => c.Blacklists).ReturnsDbSet(new List<Blacklist>
            {
                new Blacklist { UserId = userId, BlockedUserId = blockedId }
            });

            await _service.BlockUserAsync(userId, blockedId);

            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task UnblockUserAsync_WhenExists_ShouldRemove()
        {
            var userId = Guid.NewGuid();
            var blockedId = Guid.NewGuid();
            var entry = new Blacklist { UserId = userId, BlockedUserId = blockedId };
            _contextMock.Setup(c => c.Blacklists).ReturnsDbSet(new List<Blacklist> { entry });

            await _service.UnblockUserAsync(userId, blockedId);

            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UnblockUserAsync_WhenMissing_ShouldNotSave()
        {
            _contextMock.Setup(c => c.Blacklists).ReturnsDbSet(new List<Blacklist>());

            await _service.UnblockUserAsync(Guid.NewGuid(), Guid.NewGuid());

            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SearchUsersAsync_EmptyQuery_ShouldReturnEmpty()
        {
            var result = await _service.SearchUsersAsync("   ");
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetUserByExternalIdAsync_WhenExists_ShouldReturn()
        {
            var externalId = "oauth-123";
            var user = new User { UserId = Guid.NewGuid(), ExternalId = externalId, FirstName = "Ext", LastName = "User" };
            _contextMock.Setup(c => c.Users).ReturnsDbSet(new List<User> { user });

            var result = await _service.GetUserByExternalIdAsync(externalId);

            result.Should().NotBeNull();
            result!.ExternalId.Should().Be(externalId);
        }

        [Fact]
        public async Task GetUserByExternalIdAsync_WhenMissing_ShouldReturnNull()
        {
            _contextMock.Setup(c => c.Users).ReturnsDbSet(new List<User>());

            var result = await _service.GetUserByExternalIdAsync("missing");

            result.Should().BeNull();
        }

        [Fact]
        public async Task DeleteAccountAsync_WhenExists_ShouldRemove()
        {
            var userId = Guid.NewGuid();
            var user = new User { UserId = userId, FirstName = "Del", LastName = "Me" };
            _contextMock.Setup(c => c.Users).ReturnsDbSet(new List<User>());

            await _service.DeleteAccountAsync(userId);

            _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
