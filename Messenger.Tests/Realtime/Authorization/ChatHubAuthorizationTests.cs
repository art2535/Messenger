using System.Security.Claims;
using Messenger.Application.DTOs.Users;
using Messenger.Application.Interfaces;
using Messenger.Core.Models;
using Messenger.Realtime.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Messenger.Tests.Realtime.Authorization;

public sealed class ChatHubAuthorizationTests
{
    [Fact]
    public async Task JoinChat_WhenCallerIsNotMember_DoesNotAddConnectionToGroup()
    {
        var userId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var users = new Mock<IUserService>();
        users.Setup(service => service.GetUserByExternalIdAsync(
                "external-user",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDto { UserId = userId, ExternalId = "external-user" });

        var chats = new Mock<IChatService>();
        chats.Setup(service => service.GetChatParticipantsAsync(
                chatId,
                userId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        var groups = new Mock<IGroupManager>();
        var context = new Mock<HubCallerContext>();
        context.SetupGet(value => value.ConnectionId).Returns("connection-1");
        context.SetupGet(value => value.User).Returns(new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim("sub", "external-user") }, "test")));
        context.SetupGet(value => value.ConnectionAborted).Returns(CancellationToken.None);

        var hub = new ChatHub(
            users.Object,
            Mock.Of<IUserStatusService>(),
            Mock.Of<IUserPresenceTracker>(),
            chats.Object,
            Mock.Of<IAccountSettingsService>(),
            NullLogger<ChatHub>.Instance,
            Mock.Of<IRateLimitService>())
        {
            Context = context.Object,
            Groups = groups.Object
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => hub.JoinChat(chatId));

        chats.Verify(service => service.GetChatParticipantsAsync(
            chatId, userId, It.IsAny<CancellationToken>()), Times.Once);
        groups.Verify(manager => manager.AddToGroupAsync(
            "connection-1", chatId.ToString(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
