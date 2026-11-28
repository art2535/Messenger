using System.Reflection;
using Messenger.API.Options;
using Messenger.API.Services;
using Messenger.Application.Files;
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

namespace Messenger.Tests.Api.Files.Cleanup;

public sealed class UploadOrphanCleanupServiceUnitTests
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
    public async Task UploadOrphanCleanup_SkipsFilesystemAndDatabaseWhenApiBaseUrlIsInvalid()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["URL:API:HTTPS"] = "not-a-url" }).Build();
        var service = new UploadOrphanCleanupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            Mock.Of<IFileStorage>(),
            NullLogger<UploadOrphanCleanupService>.Instance);

        var cleanup = typeof(UploadOrphanCleanupService).GetMethod(
            "CleanupAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(cleanup);

        var task = (Task)cleanup!.Invoke(service, new object[] { CancellationToken.None })!;
        await task;
    }




    [Fact]
    public void UploadOrphanCleanup_CollectsOnlySafeTopLevelNamesUnderExpectedPrefix()
    {
        var collect = typeof(UploadOrphanCleanupService).GetMethod(
            "CollectReferencedFileNames", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(collect);

        var urls = new string?[]
        {
            "https://cdn.example.test/uploads/keep.png?version=1",
            "/uploads/second.pdf",
            "/uploads/../private.txt",
            "/avatars/not-an-upload.png",
            null
        };

        var result = (HashSet<string>)collect!.Invoke(null, new object[]
        {
            urls, "uploads/", StringComparer.Ordinal
        })!;

        Assert.Contains("keep.png", result);
        Assert.Contains("second.pdf", result);
        Assert.DoesNotContain("../private.txt", result);
        Assert.DoesNotContain("not-an-upload.png", result);
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
