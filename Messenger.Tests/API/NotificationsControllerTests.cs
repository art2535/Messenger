using FluentAssertions;
using Messenger.API.Controllers;
using Messenger.API.Responses;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Messenger.Tests.API
{
    public class NotificationsControllerTests
    {
        private readonly Mock<INotificationService> _notificationService = new();
        private readonly Mock<IUserService> _userService = new();
        private readonly NotificationsController _controller;
        private readonly User _currentUser;

        public NotificationsControllerTests()
        {
            _currentUser = ControllerTestHelper.CreateUser(externalId: "notif-user");
            _userService.Setup(s => s.GetUserByExternalIdAsync("notif-user")).ReturnsAsync(_currentUser);
            _controller = new NotificationsController(_notificationService.Object, _userService.Object);
            ControllerTestHelper.SetUser(_controller, "notif-user");
        }

        [Fact]
        public async Task GetNotificationsAsync_ReturnsOkWithData()
        {
            var list = new List<Notification>
            {
                new Notification
                {
                    NotificationId = Guid.NewGuid(),
                    UserId = _currentUser.UserId,
                    Text = "Hello",
                    CreationDate = DateTime.UtcNow,
                    Read = false
                }
            };
            _notificationService.Setup(s => s.GetNotificationsAsync(_currentUser.UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(list);

            var result = await _controller.GetNotificationsAsync();

            result.Should().BeOfType<OkObjectResult>()
                .Which.Value.Should().BeOfType<GetNotificationsSuccessResponse>()
                .Which.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task GetNotificationsAsync_Anonymous_Unauthorized()
        {
            ControllerTestHelper.SetAnonymous(_controller);
            var result = await _controller.GetNotificationsAsync();
            result.Should().BeOfType<UnauthorizedObjectResult>();
        }
    }
}
