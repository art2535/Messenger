using Messenger.Core.Enums;
using Messenger.Core.Models;
using Messenger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Messenger.Tests.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class ModerationServiceTests
{
    private readonly PostgreSqlFixture _database;

    public ModerationServiceTests(PostgreSqlFixture database) => _database = database;

    [Fact]
    public async Task ReviewReportAsync_ConcurrentReviews_CreateOnlyOneAuditEvent()
    {
        var ownerId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var reporterId = Guid.NewGuid();
        var firstAdminId = Guid.NewGuid();
        var secondAdminId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var reportId = Guid.NewGuid();

        await using (var seed = _database.CreateContext())
        {
            seed.Users.AddRange(
                CreateUser(ownerId),
                CreateUser(senderId),
                CreateUser(reporterId),
                CreateUser(firstAdminId),
                CreateUser(secondAdminId));
            seed.Chats.Add(new Chat
            {
                ChatId = chatId,
                Name = "Moderation test",
                Type = "group",
                UserId = ownerId,
                CreationDate = DateTime.UtcNow
            });
            seed.Messages.Add(new Message
            {
                MessageId = messageId,
                ChatId = chatId,
                SenderId = senderId,
                MessageText = "test",
                SendTime = DateTime.UtcNow,
                SequenceNumber = 1,
                DeliveryStatus = MessageDeliveryStatus.Sent
            });
            seed.MessageReports.Add(new MessageReport
            {
                ReportId = reportId,
                MessageId = messageId,
                ChatId = chatId,
                ReporterId = reporterId,
                Reason = "spam",
                Status = "pending",
                CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using var firstContext = _database.CreateContext();
        await using var secondContext = _database.CreateContext();
        var firstService = new ModerationService(firstContext);
        var secondService = new ModerationService(secondContext);

        await Task.WhenAll(
            firstService.ReviewReportAsync(reportId, firstAdminId, "resolved", "first"),
            secondService.ReviewReportAsync(reportId, secondAdminId, "dismissed", "second"));

        await using var verify = _database.CreateContext();
        var report = await verify.MessageReports.SingleAsync(item => item.ReportId == reportId);
        Assert.Contains(report.Status, new[] { "resolved", "dismissed" });
        Assert.Contains(report.ReviewedBy, new Guid?[] { firstAdminId, secondAdminId });
        Assert.Equal(1, await verify.ModerationLogs.CountAsync(log => log.MessageId == messageId));
    }

    private static User CreateUser(Guid userId) => new()
    {
        UserId = userId,
        FirstName = "Moderation",
        LastName = "Test",
        RegistrationDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Account = new AccountSetting { AccountId = userId }
    };
}
