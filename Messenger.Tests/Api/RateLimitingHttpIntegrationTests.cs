using System.Security.Claims;
using Messenger.API.Extensions;
using Messenger.Application.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Messenger.Tests.Api;

public sealed class RateLimitingHttpIntegrationTests
{
    [Fact]
    public async Task ControllerEndpoint_Returns429WhenDistributedLimiterRejectsRequest()
    {
        var limiter = new Mock<IRateLimitService>();
        limiter.Setup(service => service.TryAcquireAsync(
                "http:api:integration-user",
                120,
                TimeSpan.FromMinutes(1),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<bool>(false));

        await using var app = await CreateApplicationAsync(limiter.Object);
        var response = await app.GetTestClient().GetAsync("/messages");

        Assert.Equal(StatusCodes.Status429TooManyRequests, (int)response.StatusCode);
        Assert.Contains("Слишком много запросов", await response.Content.ReadAsStringAsync());
        limiter.Verify(service => service.TryAcquireAsync(
            "http:api:integration-user",
            120,
            TimeSpan.FromMinutes(1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NonControllerEndpoint_BypassesApiRateLimiter()
    {
        var limiter = new Mock<IRateLimitService>();
        await using var app = await CreateApplicationAsync(limiter.Object);

        var response = await app.GetTestClient().GetAsync("/health");

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        limiter.Verify(service => service.TryAcquireAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static async Task<WebApplication> CreateApplicationAsync(IRateLimitService limiter)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(limiter);

        var app = builder.Build();
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("sub", "integration-user") },
                "integration-test"));
            await next();
        });
        app.UseMessengerRateLimiting();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapGet("/messages", async context =>
                await context.Response.WriteAsync("messages"))
                .WithMetadata(new ControllerActionDescriptor());
            endpoints.MapGet("/health", async context =>
                await context.Response.WriteAsync("healthy"));
        });

        await app.StartAsync();
        return app;
    }
}
