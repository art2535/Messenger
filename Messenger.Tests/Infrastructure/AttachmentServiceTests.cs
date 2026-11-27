using Messenger.Application.DTOs.Attachments;
using Messenger.Core.Enums;
using Messenger.Core.Models;
using Messenger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Messenger.Tests.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class AttachmentServiceTests
{
    private readonly PostgreSqlFixture _database;

    public AttachmentServiceTests(PostgreSqlFixture database) => _database = database;

    [Fact]
    public async Task AddAttachmentAsync_ConcurrentDuplicateEvents_AreIdempotent()
    {
        var userId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var attachment = new CreateAttachmentDto
        {
            FileName = "report.pdf",
            FileType = "application/pdf",
            SizeInBytes = 1024,
            Url = "/uploads/report.pdf"
        };

        await using (var seed = _database.CreateContext())
        {
            seed.Users.Add(new User
            {
                UserId = userId,
                FirstName = "Attachment",
                LastName = "Test",
                RegistrationDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Account = new AccountSetting { AccountId = userId }
            });
            seed.Chats.Add(new Chat
            {
                ChatId = chatId,
                Name = "Attachment test",
                Type = "private",
                UserId = userId,
                CreationDate = DateTime.UtcNow
            });
            seed.Messages.Add(new Message
            {
                MessageId = messageId,
                ChatId = chatId,
                SenderId = userId,
                MessageText = "attachment",
                HasAttachments = true,
                SendTime = DateTime.UtcNow,
                SequenceNumber = 1,
                DeliveryStatus = MessageDeliveryStatus.Sent
            });
            await seed.SaveChangesAsync();
        }

        await using var firstContext = _database.CreateContext();
        await using var secondContext = _database.CreateContext();
        var firstService = new AttachmentService(firstContext);
        var secondService = new AttachmentService(secondContext);

        await Task.WhenAll(
            firstService.AddAttachmentAsync(messageId, attachmentId, attachment),
            secondService.AddAttachmentAsync(messageId, attachmentId, attachment));

        await using var verify = _database.CreateContext();
        Assert.Equal(1, await verify.Attachments.CountAsync(item => item.AttachmentId == attachmentId));
    }

    [Fact]
    public async Task AddAttachmentAsync_ReusedIdWithDifferentPayload_IsRejected()
    {
        var userId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();

        await using (var seed = _database.CreateContext())
        {
            seed.Users.Add(new User
            {
                UserId = userId,
                FirstName = "Attachment",
                LastName = "Test",
                RegistrationDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Account = new AccountSetting { AccountId = userId }
            });
            seed.Chats.Add(new Chat
            {
                ChatId = chatId,
                Name = "Attachment test",
                Type = "private",
                UserId = userId,
                CreationDate = DateTime.UtcNow
            });
            seed.Messages.Add(new Message
            {
                MessageId = messageId,
                ChatId = chatId,
                SenderId = userId,
                MessageText = "attachment",
                SendTime = DateTime.UtcNow,
                SequenceNumber = 1,
                DeliveryStatus = MessageDeliveryStatus.Sent
            });
            await seed.SaveChangesAsync();
        }

        await using var context = _database.CreateContext();
        var service = new AttachmentService(context);
        await service.AddAttachmentAsync(messageId, attachmentId, new CreateAttachmentDto
        {
            FileName = "original.pdf",
            FileType = "application/pdf",
            SizeInBytes = 100,
            Url = "/uploads/original.pdf"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAttachmentAsync(
            messageId,
            attachmentId,
            new CreateAttachmentDto
            {
                FileName = "changed.pdf",
                FileType = "application/pdf",
                SizeInBytes = 100,
                Url = "/uploads/original.pdf"
            }));
    }
}
