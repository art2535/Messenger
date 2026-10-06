using FluentAssertions;
using Messenger.API.Controllers;
using Messenger.API.Responses;
using Messenger.Core.Hubs;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace Messenger.Tests.API
{
    public class ReactionsControllerTests
    {
        private readonly Mock<IReactionService> _reactionService = new();
        private readonly Mock<IUserService> _userService = new();
        private readonly Mock<IHubContext<ChatHub>> _hub = new();
        private readonly ReactionsController _controller;
        private readonly User _currentUser;

        public ReactionsControllerTests()
        {
            _currentUser = ControllerTestHelper.CreateUser(externalId: "react-user");
            _userService.Setup(s => s.GetUserByExternalIdAsync("react-user")).ReturnsAsync(_currentUser);

            var clients = new Mock<IHubClients>();
            var clientProxy = new Mock<IClientProxy>();
            clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);
            _hub.Setup(h => h.Clients).Returns(clients.Object);

            _controller = new ReactionsController(_reactionService.Object, _userService.Object, _hub.Object);
            ControllerTestHelper.SetUser(_controller, "react-user");
        }

        [Fact]
        public async Task GetReactionsByMessageAsync_ReturnsOk()
        {
            var messageId = Guid.NewGuid();
            var reactions = new List<Reaction>
            {
                new Reaction
                {
                    ReactionId = Guid.NewGuid(),
                    MessageId = messageId,
                    UserId = _currentUser.UserId,
                    ReactionType = "👍",
                    User = _currentUser
                }
            };
            _reactionService.Setup(s => s.GetReactionsByMessageIdAsync(messageId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(reactions);

            var result = await _controller.GetReactionsByMessageAsync(messageId);

            result.Should().BeOfType<OkObjectResult>()
                .Which.Value.Should().BeOfType<GetReactionsSuccessResponse>()
                .Which.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task DeleteReactionAsync_MessageMissing_ReturnsNotFound()
        {
            var messageId = Guid.NewGuid();
            _reactionService.Setup(s => s.MessageExistsAsync(messageId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var result = await _controller.DeleteReactionAsync(messageId);

            result.Should().BeOfType<NotFoundObjectResult>()
                .Which.Value.Should().BeOfType<ErrorResponse>()
                .Which.Error.Should().Contain("не найдено");
        }

        [Fact]
        public async Task DeleteReactionAsync_WhenExists_DeletesAndReturnsOk()
        {
            var messageId = Guid.NewGuid();
            var chatId = Guid.NewGuid();
            _reactionService.Setup(s => s.MessageExistsAsync(messageId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _reactionService.Setup(s => s.GetChatIdByMessageIdAsync(messageId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(chatId);

            var result = await _controller.DeleteReactionAsync(messageId);

            result.Should().BeOfType<OkObjectResult>()
                .Which.Value.Should().BeOfType<DeleteReactionSuccessResponse>()
                .Which.IsSuccess.Should().BeTrue();

            _reactionService.Verify(s => s.DeleteReactionAsync(messageId, _currentUser.UserId, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
