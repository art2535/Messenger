using FluentAssertions;
using Messenger.API.Controllers;
using Messenger.API.Responses;
using Messenger.Core.DTOs.Logins;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Messenger.Tests.API
{
    public class LoginsControllerTests
    {
        private readonly Mock<ILoginService> _loginService = new();
        private readonly Mock<IUserService> _userService = new();
        private readonly LoginsController _controller;
        private readonly User _currentUser;

        public LoginsControllerTests()
        {
            _currentUser = ControllerTestHelper.CreateUser(externalId: "login-user");
            _userService.Setup(s => s.GetUserByExternalIdAsync("login-user")).ReturnsAsync(_currentUser);
            _controller = new LoginsController(_loginService.Object, _userService.Object);
            ControllerTestHelper.SetUser(_controller, "login-user");
        }

        [Fact]
        public async Task GetLoginsAsync_ReturnsUserLogins()
        {
            var logins = new List<Login>
            {
                new Login { LoginId = Guid.NewGuid(), UserId = _currentUser.UserId, Token = "t", IpAddress = "1.1.1.1", Active = true }
            };
            _loginService.Setup(s => s.GetLoginsByUserIdAsync(_currentUser.UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(logins);

            var result = await _controller.GetLoginsAsync();

            result.Should().BeOfType<OkObjectResult>()
                .Which.Value.Should().BeOfType<GetLoginsSuccessResponse>()
                .Which.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task LoginAsync_CreatesLogin()
        {
            var request = new CreateLoginRequest
            {
                Token = "abcdefghijklmnopqrstuvwxyz",
                IpAddress = "127.0.0.1"
            };

            var result = await _controller.LoginAsync(request);

            result.Should().BeOfType<OkObjectResult>()
                .Which.Value.Should().BeOfType<CreateLoginSuccessResponse>()
                .Which.IsSuccess.Should().BeTrue();

            _loginService.Verify(s => s.AddLoginAsync(
                It.Is<Login>(l => l.UserId == _currentUser.UserId && l.Active),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task LogoutAsync_DeactivatesActiveLogin()
        {
            var active = new Login
            {
                LoginId = Guid.NewGuid(),
                UserId = _currentUser.UserId,
                Token = "tok",
                IpAddress = "1.1.1.1",
                Active = true
            };
            _loginService.Setup(s => s.GetLoginsByUserIdAsync(_currentUser.UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Login> { active });

            var result = await _controller.LogoutAsync();

            result.Should().BeOfType<OkObjectResult>()
                .Which.Value.Should().BeOfType<LogoutSuccessResponse>()
                .Which.IsSuccess.Should().BeTrue();

            active.Active.Should().BeFalse();
            _loginService.Verify(s => s.UpdateLoginAsync(active, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task LogoutAsync_Anonymous_Unauthorized()
        {
            ControllerTestHelper.SetAnonymous(_controller);
            var result = await _controller.LogoutAsync();
            result.Should().BeOfType<UnauthorizedObjectResult>();
        }
    }
}
