using Messenger.Core.Enums;
using Messenger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Messenger.Tests.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class ForeignKeyDeleteBehaviorTests
{
    private readonly PostgreSqlFixture _database;

    public ForeignKeyDeleteBehaviorTests(PostgreSqlFixture database) => _database = database;

    [Fact]
    public async Task DeletingMessage_CascadesAttachmentsReactionsAndReports()
    {
        var senderId = Guid.NewGuid();
        var reporterId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var reactionId = Guid.NewGuid();
        var reportId = Guid.NewGuid();

        await using (var seed = _database.CreateContext())
        {
            seed.Users.AddRange(CreateUser(senderId), CreateUser(reporterId));
            seed.Chats.Add(new Chat
            {
                ChatId = chatId,
                Name = "FK message test",
                Type = "group",
                UserId = senderId,
                CreationDate = DateTime.UtcNow
            });
            seed.Messages.Add(new Message
            {
                MessageId = messageId,
                ChatId = chatId,
                SenderId = senderId,
                MessageText = "delete graph",
                SendTime = DateTime.UtcNow,
                SequenceNumber = 1,
                DeliveryStatus = MessageDeliveryStatus.Sent
            });
            seed.Attachments.Add(new Attachment
            {
                AttachmentId = attachmentId,
                MessageId = messageId,
                FileName = "file.txt",
                FileType = "text/plain",
                SizeInBytes = 10,
                Url = "/uploads/file.txt"
            });
            seed.Reactions.Add(new Reaction
            {
                ReactionId = reactionId,
                MessageId = messageId,
                UserId = reporterId,
                ReactionType = "👍"
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

        await using (var context = _database.CreateContext())
        {
            var message = await context.Messages.SingleAsync(x => x.MessageId == messageId);
            context.Messages.Remove(message);
            await context.SaveChangesAsync();
        }

        await using var verify = _database.CreateContext();
        Assert.False(await verify.Attachments.AnyAsync(x => x.AttachmentId == attachmentId));
        Assert.False(await verify.Reactions.AnyAsync(x => x.ReactionId == reactionId));
        Assert.False(await verify.MessageReports.AnyAsync(x => x.ReportId == reportId));
    }

    [Fact]
    public async Task DeletingChat_CascadesMessagesParticipantsAndFolderMembershipButKeepsFolder()
    {
        var userId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var folderChatId = Guid.NewGuid();

        await using (var seed = _database.CreateContext())
        {
            seed.Users.Add(CreateUser(userId));
            seed.Chats.Add(new Chat
            {
                ChatId = chatId,
                Name = "FK chat test",
                Type = "group",
                UserId = userId,
                CreationDate = DateTime.UtcNow
            });
            seed.ChatParticipants.Add(new ChatParticipant
            {
                ChatId = chatId,
                UserId = userId,
                Role = "владелец",
                JoinDate = DateTime.UtcNow
            });
            seed.ChatFolders.Add(new ChatFolder
            {
                FolderId = folderId,
                UserId = userId,
                Name = "Keep folder",
                SortOrder = 0,
                CreatedAt = DateTime.UtcNow
            });
            seed.ChatFolderChats.Add(new ChatFolderChat
            {
                Id = folderChatId,
                FolderId = folderId,
                ChatId = chatId
            });
            seed.Messages.Add(new Message
            {
                MessageId = messageId,
                ChatId = chatId,
                SenderId = userId,
                MessageText = "cascade",
                SendTime = DateTime.UtcNow,
                SequenceNumber = 1,
                DeliveryStatus = MessageDeliveryStatus.Sent
            });
            await seed.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var chat = await context.Chats.SingleAsync(x => x.ChatId == chatId);
            context.Chats.Remove(chat);
            await context.SaveChangesAsync();
        }

        await using var verify = _database.CreateContext();
        Assert.False(await verify.Chats.AnyAsync(x => x.ChatId == chatId));
        Assert.False(await verify.ChatParticipants.AnyAsync(x => x.ChatId == chatId));
        Assert.False(await verify.Messages.AnyAsync(x => x.MessageId == messageId));
        Assert.False(await verify.ChatFolderChats.AnyAsync(x => x.Id == folderChatId));
        Assert.True(await verify.ChatFolders.AnyAsync(x => x.FolderId == folderId));
    }

    [Fact]
    public async Task DeletingBroadcastSender_SetsSenderNullAndPreservesBroadcastAndRecipients()
    {
        var senderId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var broadcastId = Guid.NewGuid();

        await using (var seed = _database.CreateContext())
        {
            seed.Users.AddRange(CreateUser(senderId), CreateUser(recipientId));
            seed.Broadcasts.Add(new Broadcast
            {
                BroadcastId = broadcastId,
                Title = "Historical notice",
                MessageText = "Keep this",
                SenderId = senderId,
                CreatedAt = DateTime.UtcNow,
                TotalRecipients = 1
            });
            seed.BroadcastRecipients.Add(new BroadcastRecipient
            {
                BroadcastId = broadcastId,
                UserId = recipientId,
                SentAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var sender = await context.Users.SingleAsync(x => x.UserId == senderId);
            context.Users.Remove(sender);
            await context.SaveChangesAsync();
        }

        await using var verify = _database.CreateContext();
        var broadcast = await verify.Broadcasts.SingleAsync(x => x.BroadcastId == broadcastId);
        Assert.Null(broadcast.SenderId);
        Assert.True(await verify.BroadcastRecipients.AnyAsync(x =>
            x.BroadcastId == broadcastId && x.UserId == recipientId));
    }

    private static User CreateUser(Guid userId) => new()
    {
        UserId = userId,
        FirstName = "Foreign",
        LastName = "Key",
        RegistrationDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Account = new AccountSetting { AccountId = userId }
    };
}
