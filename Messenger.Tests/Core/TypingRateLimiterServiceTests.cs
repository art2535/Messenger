using FluentAssertions;
using Messenger.Core.Services;

namespace Messenger.Tests.Core
{
    public class TypingRateLimiterServiceTests
    {
        [Fact]
        public async Task TryAcquireAsync_UnderLimit_ShouldSucceed()
        {
            var service = new TypingRateLimiterService();
            var userId = Guid.NewGuid().ToString();

            for (int i = 0; i < 60; i++)
            {
                (await service.TryAcquireAsync(userId)).Should().BeTrue($"attempt {i + 1}");
            }
        }

        [Fact]
        public async Task TryAcquireAsync_OverLimit_ShouldFail()
        {
            var service = new TypingRateLimiterService();
            var userId = Guid.NewGuid().ToString();

            for (int i = 0; i < 60; i++)
                await service.TryAcquireAsync(userId);

            (await service.TryAcquireAsync(userId)).Should().BeFalse();
        }

        [Fact]
        public async Task TryAcquireAsync_DifferentUsers_HaveIndependentLimits()
        {
            var service = new TypingRateLimiterService();
            var userA = "user-a";
            var userB = "user-b";

            for (int i = 0; i < 60; i++)
                (await service.TryAcquireAsync(userA)).Should().BeTrue();

            (await service.TryAcquireAsync(userA)).Should().BeFalse();
            (await service.TryAcquireAsync(userB)).Should().BeTrue();
        }
    }
}
