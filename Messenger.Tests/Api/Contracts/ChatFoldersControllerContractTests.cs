using System.Security.Claims;
using Messenger.API.Controllers;
using Messenger.Application.DTOs.Users;
using Messenger.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Messenger.Tests.Api.Contracts;

public sealed class ChatFoldersControllerContractTests
{
    [Fact]
    public async Task GetById_WhenFolderDoesNotExist_ReturnsNotFoundContract()
    {
        var userId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var users = new Mock<IUserService>();
        users.Setup(service => service.GetUserByExternalIdAsync("contract-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDto { UserId = userId, ExternalId = "contract-user" });
        var folders = new Mock<IChatFolderService>();
        folders.Setup(service => service.GetFolderAsync(userId, folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Messenger.Application.DTOs.ChatFolders.ChatFolderDto?)null);

        var controller = CreateController(users.Object, folders.Object, "contract-user");

        var result = await controller.GetByIdAsync(folderId);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
        Assert.NotNull(notFound.Value);
        folders.Verify(service => service.GetFolderAsync(userId, folderId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAll_WhenExternalIdentityIsMissing_ReturnsUnauthorizedContract()
    {
        var users = new Mock<IUserService>();
        var folders = new Mock<IChatFolderService>();
        var controller = CreateController(users.Object, folders.Object, null);

        var result = await controller.GetAllAsync();

        Assert.IsType<UnauthorizedObjectResult>(result);
        folders.Verify(service => service.GetFoldersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Controller_DeclaresVersionedAuthorizedJsonRoute()
    {
        var type = typeof(ChatFoldersController);
        var route = Assert.Single(type.GetCustomAttributes(typeof(RouteAttribute), inherit: true).Cast<RouteAttribute>());

        Assert.Equal("api/v{version:apiVersion}/chat-folders", route.Template);
        Assert.NotNull(type.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).SingleOrDefault());
        Assert.NotNull(type.GetCustomAttributes(typeof(ApiControllerAttribute), inherit: true).SingleOrDefault());
    }

    private static ChatFoldersController CreateController(
        IUserService users,
        IChatFolderService folders,
        string? externalId)
    {
        var controller = new ChatFoldersController(
            folders,
            users,
            NullLogger<ChatFoldersController>.Instance);
        var identity = externalId is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity(new[] { new Claim("sub", externalId) }, "contract-test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
        return controller;
    }
}
