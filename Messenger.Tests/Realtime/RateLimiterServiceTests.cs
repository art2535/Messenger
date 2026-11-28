using Messenger.Realtime.Services;
using Xunit;

namespace Messenger.Tests.Realtime;

public sealed class RateLimiterServiceTests
{
    [Fact]
    public async Task TypingRateLimiter_EnforcesPerUserWindowAndSeparatesUsers()
    {
        using var limiter = new TypingRateLimiterService();

        for (var i = 0; i < 60; i++)
            Assert.True(await limiter.TryAcquireAsync("user-a"));

        Assert.False(await limiter.TryAcquireAsync("user-a"));
        Assert.True(await limiter.TryAcquireAsync("user-b"));
    }

    [Fact]
    public async Task SendMessageRateLimiter_SeparatesUsersAndSupportsCancellationWhenQueueIsFull()
    {
        using var limiter = new SendMessageRateLimiterService();

        for (var i = 0; i < 30; i++)
            Assert.True(await limiter.TryAcquireAsync("user-a"));

        Assert.True(await limiter.TryAcquireAsync("user-b"));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await limiter.TryAcquireAsync("user-a", cancellation.Token));
    }
}
