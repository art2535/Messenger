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

namespace Messenger.Tests.Api;

public sealed class BackgroundServiceTests
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


    [Fact]
    public async Task ScheduledMessageDispatcher_PublishesPayloadWithResolvedSenderAndNotifiesSenderGroup()
    {
        var senderId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var scheduledId = Guid.NewGuid();
        var resultMessageId = Guid.NewGuid();
        var item = new ScheduledMessageDispatchDto
        {
            ScheduledMessageId = scheduledId,
            ChatId = chatId,
            SenderId = senderId,
            EncryptedMessageText = "encrypted-body",
            HasAttachments = false,
            ScheduledAt = DateTime.UtcNow.AddMinutes(-1)
        };

        var scheduled = new Mock<IScheduledMessageService>();
        scheduled.Setup(x => x.DispatchDueAsync(
                It.IsAny<Func<ScheduledMessageDispatchDto, CancellationToken, Task<Guid>>>(),
                It.IsAny<DateTime>(), 50, It.IsAny<CancellationToken>()))
            .Returns((Func<ScheduledMessageDispatchDto, CancellationToken, Task<Guid>> publish,
                DateTime now, int limit, CancellationToken ct) =>
                DispatchOneAsync(publish, item, ct));

        var users = new Mock<IUserService>();
        users.Setup(x => x.GetUserByIdAsync(senderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDto
            {
                UserId = senderId,
                FirstName = "Scheduled",
                LastName = "Sender"
            });

        ChatMessageSent? published = null;
        var publisher = new Mock<IPublishEndpoint>();
        publisher.Setup(x => x.Publish(It.IsAny<ChatMessageSent>(), It.IsAny<CancellationToken>()))
            .Callback<ChatMessageSent, CancellationToken>((message, _) => published = message)
            .Returns(Task.CompletedTask);

        var proxy = new Mock<IClientProxy>();
        proxy.Setup(x => x.SendCoreAsync(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.Group($"User_{senderId}")).Returns(proxy.Object);
        var hub = new Mock<IHubContext<ChatHub>>();
        hub.SetupGet(x => x.Clients).Returns(clients.Object);

        using var provider = new ServiceCollection()
            .AddSingleton(scheduled.Object)
            .AddSingleton(users.Object)
            .AddSingleton(publisher.Object)
            .AddSingleton(hub.Object)
            .BuildServiceProvider();

        var service = new ScheduledMessageDispatcherService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ScheduledMessageDispatcherService>.Instance);
        var process = typeof(ScheduledMessageDispatcherService).GetMethod(
            "ProcessDueAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(process);

        await (Task)process!.Invoke(service, new object[] { CancellationToken.None })!;

        Assert.NotNull(published);
        Assert.Equal(chatId, published!.ChatId);
        Assert.Equal(senderId, published.SenderId);
        Assert.Equal("Scheduled Sender", published.SenderName);
        Assert.Equal("encrypted-body", published.MessageText);
        publisher.Verify(x => x.Publish(It.IsAny<ChatMessageSent>(), It.IsAny<CancellationToken>()), Times.Once);
        proxy.Verify(x => x.SendCoreAsync(
            "ScheduledMessageSent", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task<List<(ScheduledMessageDispatchDto Message, Guid ResultMessageId)>> DispatchOneAsync(
        Func<ScheduledMessageDispatchDto, CancellationToken, Task<Guid>> publish,
        ScheduledMessageDispatchDto item,
        CancellationToken cancellationToken)
    {
        var resultId = await publish(item, cancellationToken);
        return new List<(ScheduledMessageDispatchDto Message, Guid ResultMessageId)> { (item, resultId) };
    }


    [Fact]
    public async Task LogRetentionService_WhenDisabledCompletesWithoutResolvingDatabaseScope()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var service = new LogRetentionService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new LogRetentionOptions { Enabled = false }),
            NullLogger<LogRetentionService>.Instance);

        await service.StartAsync(CancellationToken.None);

        Assert.NotNull(service.ExecuteTask);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(service.ExecuteTask.IsCompletedSuccessfully);
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

    [Fact]
    public async Task UploadOrphanCleanup_SkipsFilesystemAndDatabaseWhenApiBaseUrlIsInvalid()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["URL:API:HTTPS"] = "not-a-url" }).Build();
        var service = new UploadOrphanCleanupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<UploadOrphanCleanupService>.Instance);

        var cleanup = typeof(UploadOrphanCleanupService).GetMethod(
            "CleanupAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(cleanup);

        var task = (Task)cleanup!.Invoke(service, new object[] { CancellationToken.None })!;
        await task;
    }


    [Fact]
    public void UploadOrphanCleanup_DeletesOnlyExpiredUnreferencedFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"messenger-cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var referencedPath = Path.Combine(directory, "referenced.bin");
        var orphanPath = Path.Combine(directory, "orphan.bin");
        var recentPath = Path.Combine(directory, "recent.bin");

        try
        {
            File.WriteAllText(referencedPath, "keep");
            File.WriteAllText(orphanPath, "delete");
            File.WriteAllText(recentPath, "recent");
            var expiredAt = DateTime.UtcNow.AddDays(-31);
            File.SetLastWriteTimeUtc(referencedPath, expiredAt);
            File.SetLastWriteTimeUtc(orphanPath, expiredAt);

            var cleanup = typeof(UploadOrphanCleanupService).GetMethod(
                "CleanupDirectory", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(cleanup);

            var deleted = (int)cleanup!.Invoke(null, new object[]
            {
                directory,
                new HashSet<string>(new[] { "referenced.bin" }, StringComparer.Ordinal),
                NullLogger<UploadOrphanCleanupService>.Instance,
                CancellationToken.None
            })!;

            Assert.Equal(1, deleted);
            Assert.True(File.Exists(referencedPath));
            Assert.False(File.Exists(orphanPath));
            Assert.True(File.Exists(recentPath));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
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
