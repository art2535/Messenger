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











}
