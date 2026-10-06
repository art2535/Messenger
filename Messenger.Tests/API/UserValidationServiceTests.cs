using FluentAssertions;
using Messenger.API.Services;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace Messenger.Tests.API
{
    public class UserValidationServiceTests
    {
        private readonly Mock<IUserService> _userService = new();

        [Fact]
        public async Task GetCurrentUserOrErrorAsync_MissingSub_ReturnsUnauthorized()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity());

            var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(principal, _userService.Object);

            user.Should().BeNull();
            error.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task GetCurrentUserOrErrorAsync_UserNotInDb_ReturnsBadRequest()
        {
            var claims = new ClaimsIdentity(new[] { new Claim("sub", "missing-ext") }, "t");
            var principal = new ClaimsPrincipal(claims);
            _userService.Setup(s => s.GetUserByExternalIdAsync("missing-ext")).ReturnsAsync((User?)null);

            var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(principal, _userService.Object);

            user.Should().BeNull();
            error.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task GetCurrentUserOrErrorAsync_ValidUser_ReturnsUser()
        {
            var dbUser = ControllerTestHelper.CreateUser(externalId: "ext-ok");
            var claims = new ClaimsIdentity(new[] { new Claim("sub", "ext-ok") }, "t");
            var principal = new ClaimsPrincipal(claims);
            _userService.Setup(s => s.GetUserByExternalIdAsync("ext-ok")).ReturnsAsync(dbUser);

            var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(principal, _userService.Object);

            error.Should().BeNull();
            user.Should().BeSameAs(dbUser);
        }

        [Fact]
        public async Task GetCurrentUserAsync_WhenError_Throws()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity());
            var act = () => UserValidationService.GetCurrentUserAsync(principal, _userService.Object);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }
}
