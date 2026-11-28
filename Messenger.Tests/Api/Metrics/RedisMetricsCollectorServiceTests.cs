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

namespace Messenger.Tests.Api.Metrics;

public sealed class RedisMetricsCollectorServiceTests
{







    private static async Task<List<(ScheduledMessageDispatchDto Message, Guid ResultMessageId)>> DispatchOneAsync(
        Func<ScheduledMessageDispatchDto, CancellationToken, Task<Guid>> publish,
        ScheduledMessageDispatchDto item,
        CancellationToken cancellationToken)
    {
        var resultId = await publish(item, cancellationToken);
        return new List<(ScheduledMessageDispatchDto Message, Guid ResultMessageId)> { (item, resultId) };
    }






    [Theory]
    [InlineData("123", 123L)]
    [InlineData("-42", -42L)]
    [InlineData("invalid", 0L)]
    [InlineData(null, 0L)]
    public void RedisMetricsCollector_ParseLongHandlesValidAndInvalidValues(string? value, long expected)
    {
        var method = typeof(RedisMetricsCollectorService).GetMethod(
            "ParseLong", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Equal(expected, (long)method!.Invoke(null, new object?[] { value })!);
    }

    [Theory]
    [InlineData("keys=12,expires=3,avg_ttl=4", 12L)]
    [InlineData("expires=3,keys=7", 7L)]
    [InlineData("expires=3", 0L)]
    [InlineData("keys=bad,expires=3", 0L)]
    public void RedisMetricsCollector_ParseKeyspaceKeysExtractsOnlyKeyCount(string value, long expected)
    {
        var method = typeof(RedisMetricsCollectorService).GetMethod(
            "ParseKeyspaceKeys", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Equal(expected, (long)method!.Invoke(null, new object[] { value })!);
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
