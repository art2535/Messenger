using Messenger.Application.DTOs.Broadcasts;
using Messenger.Application.Interfaces;
using Messenger.Application.Services;
using Moq;
using Xunit;

namespace Messenger.Tests.Application;

public sealed class BroadcastServiceTests
{
    [Fact]
    public async Task CreateBroadcastAsync_ValidatesEveryRecipientBeforePersisting()
    {
        var senderId = Guid.NewGuid();
        var recipients = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var repository = new Mock<IBroadcastRepository>();
        var created = new BroadcastCreatedResponse { BroadcastId = Guid.NewGuid(), TotalRecipients = 2 };
        repository.Setup(x => x.GetExistingRecipientIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid>)recipients);
        repository.Setup(x => x.CreateBroadcastAsync(
                It.IsAny<CreateBroadcastRequest>(), senderId, recipients, It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        var service = new BroadcastService(repository.Object);
        var result = await service.CreateBroadcastAsync(new CreateBroadcastRequest
        {
            Title = "Notice",
            MessageText = "Maintenance",
            RecipientIds = recipients.ToList()
        }, senderId);

        Assert.Equal(created.BroadcastId, result.BroadcastId);
        repository.Verify(x => x.CreateBroadcastAsync(
            It.IsAny<CreateBroadcastRequest>(), senderId, recipients, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateBroadcastAsync_RejectsMissingRecipientsWithoutCreatingBroadcast()
    {
        var repository = new Mock<IBroadcastRepository>();
        repository.Setup(x => x.GetExistingRecipientIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid>)Array.Empty<Guid>());

        var service = new BroadcastService(repository.Object);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateBroadcastAsync(
            new CreateBroadcastRequest
            {
                Title = "Notice",
                MessageText = "Maintenance",
                RecipientIds = new List<Guid> { Guid.NewGuid() }
            }, Guid.NewGuid()));

        repository.Verify(x => x.CreateBroadcastAsync(
            It.IsAny<CreateBroadcastRequest>(), It.IsAny<Guid>(),
            It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetBroadcastSummaryAsync_RestrictsNonOwnerAndAllowsOwnerOrAdmin()
    {
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var summary = new BroadcastSummaryDto { BroadcastId = Guid.NewGuid(), SenderId = ownerId };
        var repository = new Mock<IBroadcastRepository>();
        repository.Setup(x => x.GetBroadcastSummaryAsync(summary.BroadcastId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);
        var service = new BroadcastService(repository.Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetBroadcastSummaryAsync(summary.BroadcastId, otherId, false));
        Assert.Same(summary, await service.GetBroadcastSummaryAsync(summary.BroadcastId, ownerId, false));
        Assert.Same(summary, await service.GetBroadcastSummaryAsync(summary.BroadcastId, otherId, true));
    }

    [Fact]
    public async Task MarkAsReadAsync_ThrowsWhenCallerIsNotARecipient()
    {
        var repository = new Mock<IBroadcastRepository>();
        repository.Setup(x => x.MarkAsReadAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MarkAsReadResponse?)null);
        var service = new BroadcastService(repository.Object);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.MarkAsReadAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task GetMyBroadcastsAsync_ForwardsUserAndUnreadFilter()
    {
        var userId = Guid.NewGuid();
        var items = new List<BroadcastInboxItemDto>();
        var repository = new Mock<IBroadcastRepository>();
        repository.Setup(x => x.GetMyBroadcastsAsync(userId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);
        var service = new BroadcastService(repository.Object);

        Assert.Same(items, await service.GetMyBroadcastsAsync(userId, unreadOnly: false));
    }
}
