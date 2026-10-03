using FluentAssertions;
using Messenger.API.Controllers;
using Messenger.API.Responses;
using Messenger.Core.DTOs.UserStatuses;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Messenger.Tests.API
{
    public class UserStatusesControllerTests
    {
        private readonly Mock<IUserStatusService> _statusService = new();
        private readonly Mock<IUserService> _userService = new();
        private readonly UserStatusesController _controller;
        private readonly User _currentUser;

        public UserStatusesControllerTests()
        {
            _currentUser = ControllerTestHelper.CreateUser(externalId: "status-user");
            _userService.Setup(s => s.GetUserByExternalIdAsync("status-user")).ReturnsAsync(_currentUser);
            _controller = new UserStatusesController(_statusService.Object, _userService.Object);
            ControllerTestHelper.SetUser(_controller, "status-user");
        }

        [Fact]
        public async Task GetUserStatusesAsync_WhenFound_ReturnsOk()
        {
            var status = new UserStatus { UserId = _currentUser.UserId, Online = true, LastActivity = DateTime.UtcNow };
            _statusService.Setup(s => s.GetUserStatusByUserIdAsync(_currentUser.UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(status);

            var result = await _controller.GetUserStatusesAsync();

            var body = result.Should().BeOfType<OkObjectResult>().Subject.Value
                .Should().BeOfType<GetUserStatusSuccessResponse>().Subject;
            body.IsSuccess.Should().BeTrue();
            body.Data.Should().BeSameAs(status);
        }

        [Fact]
        public async Task GetUserStatusesAsync_WhenMissing_ReturnsNotFound()
        {
            _statusService.Setup(s => s.GetUserStatusByUserIdAsync(_currentUser.UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((UserStatus?)null);

            var result = await _controller.GetUserStatusesAsync();

            ControllerTestHelper.GetStatusCode(result).Should().Be(404);
        }

        [Fact]
        public async Task GetUserStatusesAsync_Anonymous_ReturnsUnauthorized()
        {
            ControllerTestHelper.SetAnonymous(_controller);

            var result = await _controller.GetUserStatusesAsync();

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task UpdateStatusAsync_CallsServiceAndReturnsOk()
        {
            var request = new UpdateStatusRequest { Online = true };

            var result = await _controller.UpdateStatusAsync(request);

            result.Should().BeOfType<OkObjectResult>()
                .Which.Value.Should().BeOfType<UpdateUserStatusSuccessResponse>()
                .Which.IsSuccess.Should().BeTrue();

            _statusService.Verify(s => s.UpdateUserStatusAsync(
                It.Is<UserStatus>(u => u.UserId == _currentUser.UserId && u.Online),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
