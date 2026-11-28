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

        var host = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                services.AddAuthorization();
                services.AddSignalR();
                services.AddSingleton<IUserService>(Mock.Of<IUserService>());
                services.AddSingleton<IUserStatusService>(Mock.Of<IUserStatusService>());
                services.AddSingleton<IUserPresenceTracker>(Mock.Of<IUserPresenceTracker>());
                services.AddSingleton<IChatService>(Mock.Of<IChatService>());
                services.AddSingleton<IAccountSettingsService>(Mock.Of<IAccountSettingsService>());
                services.AddSingleton<IRateLimitService>(limiter.Object);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapHub<ChatHub>("/chat"));
            });

        using var server = new TestServer(host);
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
