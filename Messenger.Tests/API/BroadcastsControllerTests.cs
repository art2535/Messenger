using FluentAssertions;
using Messenger.API.Controllers;
using Messenger.Core.DTOs.Broadcasts;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Messenger.Tests.API
{
    public class BroadcastsControllerTests
    {
        private readonly Mock<IBroadcastService> _service = new();
        private readonly Mock<IUserService> _userService = new();
        private readonly BroadcastsController _controller;
        private readonly User _currentUser;
        private readonly string _externalAsGuid;

        public BroadcastsControllerTests()
        {
            _externalAsGuid = Guid.NewGuid().ToString();
            _currentUser = ControllerTestHelper.CreateUser(externalId: _externalAsGuid);
            _userService.Setup(s => s.GetUserByExternalIdAsync(_externalAsGuid)).ReturnsAsync(_currentUser);

            _controller = new BroadcastsController(_service.Object, _userService.Object);
            ControllerTestHelper.SetUser(_controller, _externalAsGuid);
        }

        [Fact]
        public async Task CreateBroadcastAsync_NullBody_ReturnsBadRequest()
        {
            var result = await _controller.CreateBroadcastAsync(null);
            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task CreateBroadcastAsync_Valid_ReturnsCreated()
        {
            var request = new CreateBroadcastRequest
            {
                Title = "News",
                MessageText = "Hello all",
                RecipientIds = new List<Guid> { Guid.NewGuid() }
            };
            var created = new BroadcastCreatedResponse
            {
                BroadcastId = Guid.NewGuid(),
                TotalRecipients = 1
            };
            _service.Setup(s => s.CreateBroadcastAsync(request, _currentUser.UserId)).ReturnsAsync(created);

            var result = await _controller.CreateBroadcastAsync(request);

            result.Should().BeOfType<CreatedResult>()
                .Which.Value.Should().BeSameAs(created);
        }

        [Fact]
        public async Task CreateBroadcastAsync_ArgumentException_ReturnsBadRequest()
        {
            var request = new CreateBroadcastRequest
            {
                Title = "x",
                MessageText = "y",
                RecipientIds = new List<Guid> { Guid.NewGuid() }
            };
            _service.Setup(s => s.CreateBroadcastAsync(request, _currentUser.UserId))
                .ThrowsAsync(new ArgumentException("Получатели не найдены"));

            var result = await _controller.CreateBroadcastAsync(request);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task GetMyBroadcastsAsync_ReturnsOk()
        {
            _service.Setup(s => s.GetMyBroadcastsAsync(_currentUser.UserId, true))
                .ReturnsAsync(new List<object>());

            var result = await _controller.GetMyBroadcastsAsync();

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task MarkAsReadAsync_ReturnsOk()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.MarkAsReadAsync(id, _currentUser.UserId))
                .ReturnsAsync(new MarkAsReadResponse { Success = true, ReadAt = DateTime.UtcNow });

            var result = await _controller.MarkAsReadAsync(id);

            result.Should().BeOfType<OkObjectResult>();
        }
    }
}
