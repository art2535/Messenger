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
    public class ChatsControllerTests
    {
        private readonly Mock<IChatService> _chatService = new();
        private readonly Mock<IUserService> _userService = new();
        private readonly Mock<IHubContext<ChatHub>> _hub = new();
        private readonly ChatsController _controller;
        private readonly User _currentUser;

        public ChatsControllerTests()
        {
            _currentUser = ControllerTestHelper.CreateUser(externalId: "chat-user");
            _userService.Setup(s => s.GetUserByExternalIdAsync("chat-user")).ReturnsAsync(_currentUser);

            var clients = new Mock<IHubClients>();
            var clientProxy = new Mock<IClientProxy>();
            clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);
            clients.Setup(c => c.User(It.IsAny<string>())).Returns(clientProxy.Object);
            _hub.Setup(h => h.Clients).Returns(clients.Object);

            _controller = new ChatsController(_chatService.Object, _hub.Object, _userService.Object);
            ControllerTestHelper.SetUser(_controller, "chat-user");
        }

        [Fact]
        public async Task GetChatsByIdAsync_ReturnsOk()
        {
            var chats = new List<object> { new { ChatId = Guid.NewGuid(), Name = "Team" } };
            _chatService.Setup(s => s.GetUserChatsWithLastMessageAsync(_currentUser.UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(chats);

            var result = await _controller.GetChatsByIdAsync();

            result.Should().BeOfType<OkObjectResult>()
                .Which.Value.Should().BeOfType<GetUserChatsSuccessResponse>()
                .Which.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task GetChatsByIdAsync_Anonymous_Unauthorized()
        {
            ControllerTestHelper.SetAnonymous(_controller);
            var result = await _controller.GetChatsByIdAsync();
            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task GetChatByIdAsync_WhenFound_ReturnsOk()
        {
            var chatId = Guid.NewGuid();
            var chat = new Chat { ChatId = chatId, Name = "DM", Type = "private", UserId = _currentUser.UserId };
            _chatService.Setup(s => s.GetChatByIdAsync(chatId, It.IsAny<CancellationToken>())).ReturnsAsync(chat);
            _chatService.Setup(s => s.GetChatParticipantsAsync(chatId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ChatParticipant>
                {
                    new ChatParticipant { ChatId = chatId, UserId = _currentUser.UserId, Role = "владелец" }
                });

            var result = await _controller.GetChatByIdAsync(chatId);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task GetChatByIdAsync_WhenMissing_ReturnsNotFound()
        {
            var chatId = Guid.NewGuid();
            _chatService.Setup(s => s.GetChatByIdAsync(chatId, It.IsAny<CancellationToken>())).ReturnsAsync((Chat?)null);

            var result = await _controller.GetChatByIdAsync(chatId);

            result.Should().BeOfType<NotFoundObjectResult>()
                .Which.Value.Should().BeOfType<ErrorResponse>()
                .Which.Error.Should().Contain("не найден");
        }
    }
}
