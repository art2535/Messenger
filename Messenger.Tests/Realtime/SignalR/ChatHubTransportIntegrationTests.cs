using System.Security.Claims;
using System.Text.Encodings.Web;
using Messenger.Application.Interfaces;
using Messenger.Realtime.Hubs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Messenger.Tests.Realtime.SignalR;

public sealed class ChatHubTransportIntegrationTests
{
    [Fact]
    public async Task AuthorizedClient_CanInvokeProductionHubAndUsesAuthenticatedPartition()
    {
        var limiter = new Mock<IRateLimitService>();
        limiter.Setup(service => service.TryAcquireAsync(
                "signalr:send-message:signalr-user",
                30,
                TimeSpan.FromMinutes(1),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<bool>(true));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<IUserService>(Mock.Of<IUserService>());
        builder.Services.AddSingleton<IUserStatusService>(Mock.Of<IUserStatusService>());
        builder.Services.AddSingleton<IUserPresenceTracker>(Mock.Of<IUserPresenceTracker>());
        builder.Services.AddSingleton<IChatService>(Mock.Of<IChatService>());
        builder.Services.AddSingleton<IAccountSettingsService>(Mock.Of<IAccountSettingsService>());
        builder.Services.AddSingleton<IRateLimitService>(limiter.Object);

        await using var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHub<ChatHub>("/chat");
        await app.StartAsync();

        var server = app.GetTestServer();
        await using var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/chat", options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        await connection.StartAsync();
        var acquired = await connection.InvokeAsync<bool>("TryAcquireSendMessagePermit");

        Assert.True(acquired);
        limiter.Verify(service => service.TryAcquireAsync(
            "signalr:send-message:signalr-user",
            30,
            TimeSpan.FromMinutes(1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim("sub", "signalr-user") },
                Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
