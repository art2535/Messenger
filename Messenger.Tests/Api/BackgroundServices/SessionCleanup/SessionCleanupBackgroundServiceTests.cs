using System.Reflection;
using Messenger.API.Options;
using Messenger.API.Services;
using Messenger.Application.DTOs.UserStatuses;
using Messenger.Application.DTOs.Users;
using Messenger.Application.Messages;
using Messenger.Application.DTOs.Messages;
using MassTransit;
using Messenger.Application.Interfaces;
using Messenger.Infrastructure.Services;
using Messenger.Realtime.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Messenger.Tests.Api.BackgroundServices.SessionCleanup;

public sealed class SessionCleanupBackgroundServiceTests
{
    [Fact]
    public async Task SessionCleanup_KeepsUsersWithLivePresenceAndExpiresOnlyDisconnectedUsers()
    {
        var connectedUserId = Guid.NewGuid();
        var disconnectedUserId = Guid.NewGuid();
        var status = new Mock<IUserStatusService>();
        status.Setup(x => x.GetInactiveOnlineStatusesAsync(
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<UserStatusDto>)new[]
            {
                new UserStatusDto { UserId = connectedUserId, Online = true },
                new UserStatusDto { UserId = disconnectedUserId, Online = true }
            });
        status.Setup(x => x.SetOfflineBatchAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var logins = new Mock<ILoginService>();
        logins.Setup(x => x.CloseActiveLoginsForIdleUsersAsync(
                It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var chat = new Mock<IChatService>();
        chat.Setup(x => x.GetPeerUserIdsAsync(disconnectedUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid>)new[] { disconnectedUserId });

        var presence = new Mock<IUserPresenceTracker>();
        presence.Setup(x => x.HasActiveConnectionsAsync(connectedUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        presence.Setup(x => x.HasActiveConnectionsAsync(disconnectedUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var hub = CreateHubContext();
        using var provider = CreateScopeProvider(status.Object, logins.Object, chat.Object);
        var service = CreateSessionCleanupService(provider, hub.Object, presence.Object);

        await InvokeCleanupOnceAsync(service);

        status.Verify(x => x.SetOfflineBatchAsync(
            It.Is<IEnumerable<Guid>>(ids => ids.SequenceEqual(new[] { disconnectedUserId })),
            It.IsAny<CancellationToken>()), Times.Once);
        logins.Verify(x => x.CloseActiveLoginsForIdleUsersAsync(
            It.Is<IEnumerable<Guid>>(ids => ids.SequenceEqual(new[] { disconnectedUserId })),
            It.IsAny<CancellationToken>()), Times.Once);
        chat.Verify(x => x.GetPeerUserIdsAsync(disconnectedUserId, It.IsAny<CancellationToken>()), Times.Once);
        chat.Verify(x => x.GetPeerUserIdsAsync(connectedUserId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SessionCleanup_RechecksPresenceAndRestoresOnlineWhenUserReconnectsDuringCleanup()
    {
        var userId = Guid.NewGuid();
        var status = new Mock<IUserStatusService>();
        status.Setup(x => x.GetInactiveOnlineStatusesAsync(
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<UserStatusDto>)new[]
            {
                new UserStatusDto { UserId = userId, Online = true }
            });
        status.Setup(x => x.SetOfflineBatchAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        status.Setup(x => x.SetOnlineBatchAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var presence = new Mock<IUserPresenceTracker>();
        presence.SetupSequence(x => x.HasActiveConnectionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);

        var logins = new Mock<ILoginService>();
        var chat = new Mock<IChatService>();
        var hub = CreateHubContext();
        using var provider = CreateScopeProvider(status.Object, logins.Object, chat.Object);
        var service = CreateSessionCleanupService(provider, hub.Object, presence.Object);

        await InvokeCleanupOnceAsync(service);

        status.Verify(x => x.SetOfflineBatchAsync(
            It.Is<IEnumerable<Guid>>(ids => ids.Contains(userId)), It.IsAny<CancellationToken>()), Times.Once);
        status.Verify(x => x.SetOnlineBatchAsync(
            It.Is<IEnumerable<Guid>>(ids => ids.Contains(userId)), It.IsAny<CancellationToken>()), Times.Once);
        logins.Verify(x => x.CloseActiveLoginsForIdleUsersAsync(
            It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
        chat.Verify(x => x.GetPeerUserIdsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }





















    private static Mock<IHubContext<ChatHub>> CreateHubContext()
    {
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(x => x.SendCoreAsync(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.Groups(It.IsAny<IReadOnlyList<string>>())).Returns(proxy.Object);

        var hub = new Mock<IHubContext<ChatHub>>();
        hub.SetupGet(x => x.Clients).Returns(clients.Object);
        return hub;
    }

    private static ServiceProvider CreateScopeProvider(
        IUserStatusService status,
        ILoginService logins,
        IChatService chat) =>
        new ServiceCollection()
            .AddSingleton(status)
            .AddSingleton(logins)
            .AddSingleton(chat)
            .BuildServiceProvider();

    private static SessionCleanupBackgroundService CreateSessionCleanupService(
        IServiceProvider provider,
        IHubContext<ChatHub> hub,
        IUserPresenceTracker presence) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            hub,
            Options.Create(new SessionCleanupOptions { IdleMinutes = 30, IntervalMinutes = 1 }),
            NullLogger<SessionCleanupBackgroundService>.Instance,
            new RedisDistributedLock(null, NullLogger<RedisDistributedLock>.Instance),
            presence);

    private static async Task InvokeCleanupOnceAsync(SessionCleanupBackgroundService service)
    {
        var method = typeof(SessionCleanupBackgroundService).GetMethod(
            "CleanupOnceAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = (Task)method!.Invoke(service,
            new object[] { TimeSpan.FromMinutes(30), CancellationToken.None })!;
        await task;
    }
}
