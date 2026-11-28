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















}
