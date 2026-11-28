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

namespace Messenger.Tests.Api.BackgroundServices.ScheduledMessages;

public sealed class ScheduledMessageDispatcherServiceTests
{





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
