using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Messenger.Core.Models;
using Messenger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Messenger.Tests.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class JwtServiceTests
{
    private readonly PostgreSqlFixture _database;

    public JwtServiceTests(PostgreSqlFixture database) => _database = database;

    [Fact]
    public async Task GenerateJwtTokenAsync_UsesConfiguredKeyAndPersistsUserRolesAsClaims()
    {
        var userId = Guid.NewGuid();
        var key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray());
        var previousKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY");
        Environment.SetEnvironmentVariable("JWT_SECRET_KEY", null);

        try
        {
            await using (var seed = _database.CreateContext())
            {
                seed.Users.Add(new User
                {
                    UserId = userId,
                    FirstName = "JWT",
                    LastName = "Test",
                    Login = "jwt-user",
                    RegistrationDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    Account = new AccountSetting { AccountId = userId }
                });
                await seed.SaveChangesAsync();

                var user = await seed.Users.SingleAsync(x => x.UserId == userId);
                user.Roles.Add(await seed.Roles.SingleAsync(x => x.Name == "Пользователь"));
                await seed.SaveChangesAsync();
            }

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = key,
                    ["Jwt:Issuer"] = "messenger-tests",
                    ["Jwt:Audience"] = "messenger-client"
                }).Build();

            await using var context = _database.CreateContext();
            var service = new JwtService(configuration, context);
            var encoded = await service.GenerateJwtTokenAsync(new User
            {
                UserId = userId,
                Login = "jwt-user"
            });

            var token = new JwtSecurityTokenHandler().ReadJwtToken(encoded);
            Assert.Equal("messenger-tests", token.Issuer);
            Assert.Contains("messenger-client", token.Audiences);
            Assert.Equal("HS256", token.Header.Alg);
            Assert.Contains(token.Claims, claim => claim.Value == userId.ToString());
            Assert.Contains(token.Claims, claim => claim.Type == ClaimTypes.Role && claim.Value == "Пользователь");
            Assert.InRange(token.ValidTo, DateTime.UtcNow.AddMinutes(110), DateTime.UtcNow.AddHours(2).AddMinutes(1));
        }
        finally
        {
            Environment.SetEnvironmentVariable("JWT_SECRET_KEY", previousKey);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid-base64")]
    [InlineData("AQID")]
    public async Task GenerateJwtTokenAsync_RejectsMissingMalformedOrShortKey(string key)
    {
        var previousKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY");
        Environment.SetEnvironmentVariable("JWT_SECRET_KEY", null);

        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Jwt:Key"] = key }).Build();
            await using var context = _database.CreateContext();
            var service = new JwtService(configuration, context);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.GenerateJwtTokenAsync(new User { UserId = Guid.NewGuid() }));
        }
        finally
        {
            Environment.SetEnvironmentVariable("JWT_SECRET_KEY", previousKey);
        }
    }
}
