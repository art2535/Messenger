using FluentAssertions;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Messenger.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using Moq.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Messenger.Tests.Services
{
    public class JwtServiceTests
    {
        private static readonly string ValidKeyBase64 = 
            Convert.ToBase64String(Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"));

        private readonly Mock<GuapMessengerContext> _contextMock;
        private readonly IConfiguration _configuration;
        private readonly JwtService _service;

        public JwtServiceTests()
        {
            _contextMock = new Mock<GuapMessengerContext>();
            _contextMock.Setup(c => c.Users).ReturnsDbSet(new List<User>());

            var dict = new Dictionary<string, string?>
            {
                ["Jwt:Key"] = ValidKeyBase64,
                ["Jwt:Issuer"] = "Messenger.Tests",
                ["Jwt:Audience"] = "Messenger.Clients"
            };
            _configuration = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
            _service = new JwtService(_configuration, _contextMock.Object);
        }

        [Fact]
        public async Task GenerateJwtTokenAsync_ValidUser_ShouldReturnParsableToken()
        {
            var user = new User
            {
                UserId = Guid.NewGuid(),
                Login = "testuser",
                FirstName = "Test",
                LastName = "User",
                Roles = new List<Role>()
            };

            var users = new List<User> { user };
            _contextMock.Setup(c => c.Users).ReturnsDbSet(users);

            var token = await _service.GenerateJwtTokenAsync(user);

            token.Should().NotBeNullOrWhiteSpace();
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);

            jwt.Issuer.Should().Be("Messenger.Tests");
            jwt.Audiences.Should().Contain("Messenger.Clients");
            jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.NameIdentifier && c.Value == user.UserId.ToString());
            jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Name && c.Value == "testuser");
        }

        [Fact]
        public async Task GenerateJwtTokenAsync_MissingKey_ShouldThrow()
        {
            var emptyConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
            var service = new JwtService(emptyConfig, _contextMock.Object);
            var user = new User { UserId = Guid.NewGuid(), Login = "x" };

            var act = () => service.GenerateJwtTokenAsync(user);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*JWT ключ не найден*");
        }

        [Fact]
        public async Task GenerateJwtTokenAsync_InvalidBase64Key_ShouldThrow()
        {
            var badConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "not-valid-base64!!!"
            }).Build();
            var service = new JwtService(badConfig, _contextMock.Object);
            var user = new User { UserId = Guid.NewGuid(), Login = "x" };

            var act = () => service.GenerateJwtTokenAsync(user);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Base64*");
        }
    }
}
