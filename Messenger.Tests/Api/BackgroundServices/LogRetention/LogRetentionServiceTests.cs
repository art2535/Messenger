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

namespace Messenger.Tests.Api.BackgroundServices.LogRetention;

public sealed class LogRetentionServiceTests
{









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

















}
