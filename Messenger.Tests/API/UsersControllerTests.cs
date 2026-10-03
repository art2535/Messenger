using FluentAssertions;
using Messenger.API.Controllers;
using Messenger.API.Responses;
using Messenger.Core.DTOs.Users;
using Messenger.Core.Hubs;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace Messenger.Tests.API
{
    public class UsersControllerTests
    {
        private readonly Mock<IUserService> _userService = new();
        private readonly Mock<IHubContext<ChatHub>> _hub = new();
        private readonly UsersController _controller;

        public UsersControllerTests()
        {
            var clients = new Mock<IHubClients>();
            var clientProxy = new Mock<IClientProxy>();
            clients.Setup(c => c.User(It.IsAny<string>())).Returns(clientProxy.Object);
            _hub.Setup(h => h.Clients).Returns(clients.Object);
            _controller = new UsersController(_userService.Object, _hub.Object);
        }

        [Fact]
        public async Task GetAllUsersAsync_ReturnsOk()
        {
            var users = new List<User>
            {
                ControllerTestHelper.CreateUser(),
                ControllerTestHelper.CreateUser()
            };
            _userService.Setup(s => s.GetAllUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(users);

            var result = await _controller.GetAllUsersAsync();

            var body = result.Should().BeOfType<OkObjectResult>().Subject.Value
                .Should().BeOfType<GetAllUsersSuccessResponse>().Subject;
            body.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task GetAllRolesAsync_ReturnsOk()
        {
            var roles = new List<Role>
            {
                new Role { RoleId = Guid.NewGuid(), Name = "ROLE_USER", Description = "User" }
            };
            _userService.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(roles);

            var result = await _controller.GetAllRolesAsync();

            result.Should().BeOfType<OkObjectResult>()
                .Which.Value.Should().BeOfType<GetRolesSuccessResponse>()
                .Which.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task SearchAsync_ShortQuery_ReturnsEmptyData()
        {
            var result = await _controller.SearchAsync("a");

            var body = result.Should().BeOfType<OkObjectResult>().Subject.Value
                .Should().BeOfType<SearchUsersSuccessResponse>().Subject;
            body.IsSuccess.Should().BeTrue();
            _userService.Verify(s => s.SearchUsersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SearchAsync_ValidQuery_CallsService()
        {
            _userService.Setup(s => s.SearchUsersAsync("ivan", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<UserSearch>
                {
                    new UserSearch { Id = Guid.NewGuid(), Name = "Иван Тестов" }
                });

            var result = await _controller.SearchAsync("ivan");

            result.Should().BeOfType<OkObjectResult>();
            _userService.Verify(s => s.SearchUsersAsync("ivan", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetUserByExternalIdAsync_ReturnsUser()
        {
            var user = ControllerTestHelper.CreateUser(externalId: "ext-99");
            _userService.Setup(s => s.GetUserByExternalIdAsync("ext-99")).ReturnsAsync(user);

            var result = await _controller.GetUserByExternalIdAsync("ext-99");

            result.Should().BeOfType<OkObjectResult>()
                .Which.Value.Should().BeSameAs(user);
        }
    }
}
