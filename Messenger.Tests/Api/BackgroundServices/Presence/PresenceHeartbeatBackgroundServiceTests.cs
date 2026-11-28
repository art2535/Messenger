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

namespace Messenger.Tests.Api.BackgroundServices.Presence;

public sealed class PresenceHeartbeatBackgroundServiceTests
{







    private static async Task<List<(ScheduledMessageDispatchDto Message, Guid ResultMessageId)>> DispatchOneAsync(
        Func<ScheduledMessageDispatchDto, CancellationToken, Task<Guid>> publish,
        ScheduledMessageDispatchDto item,
        CancellationToken cancellationToken)
    {
        var resultId = await publish(item, cancellationToken);
        return new List<(ScheduledMessageDispatchDto Message, Guid ResultMessageId)> { (item, resultId) };
    }




    [Fact]
    public async Task PresenceHeartbeatBackgroundService_StopsCleanlyBeforeFirstHeartbeat()
    {
        var tracker = new Mock<IUserPresenceTracker>();
        var service = new PresenceHeartbeatBackgroundService(
            tracker.Object,
            NullLogger<PresenceHeartbeatBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        tracker.Verify(x => x.HeartbeatAsync(It.IsAny<CancellationToken>()), Times.Never);
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
