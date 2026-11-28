using System.Text.Json;
using Messenger.API.Services;
using Messenger.Application.Files;
using Messenger.Core.Enums;
using Messenger.Core.Models;
using Messenger.Core.Messages;
using Messenger.Infrastructure.Data;
using Messenger.Infrastructure.Files;
using Messenger.Tests.Shared.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Messenger.Tests.Api.Files.Cleanup;

[Collection(PostgreSqlCollection.Name)]
public sealed class UploadOrphanCleanupServiceTests : IDisposable
{
    private readonly PostgreSqlFixture _database;
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(), "Messenger.Tests", Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorage _storage;

    public UploadOrphanCleanupServiceTests(PostgreSqlFixture database)
    {
        _database = database;
        _storage = new LocalFileStorage(_rootPath);
    }

    [Fact]
    public async Task CleanupNowAsync_DeletesOldOrphansAndPreservesReferencedAndRecentFiles()
    {
        const string apiBase = "https://api.example.test";
        var userId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();

        await SaveFileAsync(FileStorageArea.Uploads, "orphan-old.pdf");
        await SaveFileAsync(FileStorageArea.Uploads, "referenced.pdf");
        await SaveFileAsync(FileStorageArea.Uploads, "scheduled.pdf");
        await SaveFileAsync(FileStorageArea.Uploads, "recent.pdf");
        await SaveFileAsync(FileStorageArea.Avatars, "orphan-avatar.png");
        await SaveFileAsync(FileStorageArea.Avatars, "referenced-avatar.png");

        SetOld("uploads", "orphan-old.pdf");
        SetOld("uploads", "referenced.pdf");
        SetOld("uploads", "scheduled.pdf");
        SetOld("avatars", "orphan-avatar.png");
        SetOld("avatars", "referenced-avatar.png");

        await using (var context = _database.CreateContext())
        {
            context.Users.Add(new User
            {
                UserId = userId,
                FirstName = "File",
                LastName = "Cleanup",
                RegistrationDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Account = new AccountSetting
                {
                    AccountId = userId,
                    Avatar = $"{apiBase}/avatars/referenced-avatar.png"
                }
            });
            context.Chats.Add(new Chat
            {
                ChatId = chatId,
                Name = "Cleanup test",
                Type = "private",
                UserId = userId,
                CreationDate = DateTime.UtcNow
            });
            context.Messages.Add(new Message
            {
                MessageId = messageId,
                ChatId = chatId,
                SenderId = userId,
                MessageText = "file cleanup",
                SendTime = DateTime.UtcNow,
                SequenceNumber = 1,
                DeliveryStatus = MessageDeliveryStatus.Sent
            });
            context.Attachments.Add(new Attachment
            {
                AttachmentId = attachmentId,
                MessageId = messageId,
                FileName = "referenced.pdf",
                FileType = "application/pdf",
                SizeInBytes = 2,
                Url = $"{apiBase}/uploads/referenced.pdf"
            });
            context.ScheduledMessages.Add(new ScheduledMessage
            {
                ScheduledMessageId = Guid.NewGuid(),
                ChatId = chatId,
                SenderId = userId,
                AttachmentsJson = JsonSerializer.Serialize(new[]
                {
                    new AttachmentInfo
                    {
                        FileName = "scheduled.pdf",
                        FileType = "application/pdf",
                        SizeInBytes = 2,
                        Url = $"{apiBase}/uploads/scheduled.pdf"
                    }
                }),
                HasAttachments = true,
                ScheduledAt = DateTime.UtcNow.AddHours(1),
                CreatedAt = DateTime.UtcNow,
                Status = ScheduledMessageStatus.Pending
            });
            await context.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddScoped<GuapMessengerContext>(_ => _database.CreateContext());
        using var provider = services.BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["URL:API:HTTPS"] = apiBase
            })
            .Build();

        using var cleanup = new UploadOrphanCleanupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            _storage,
            NullLogger<UploadOrphanCleanupService>.Instance);

        await cleanup.CleanupNowAsync();

        Assert.False(await _storage.ExistsAsync(FileStorageArea.Uploads, "orphan-old.pdf"));
        Assert.True(await _storage.ExistsAsync(FileStorageArea.Uploads, "referenced.pdf"));
        Assert.True(await _storage.ExistsAsync(FileStorageArea.Uploads, "scheduled.pdf"));
        Assert.True(await _storage.ExistsAsync(FileStorageArea.Uploads, "recent.pdf"));
        Assert.False(await _storage.ExistsAsync(FileStorageArea.Avatars, "orphan-avatar.png"));
        Assert.True(await _storage.ExistsAsync(FileStorageArea.Avatars, "referenced-avatar.png"));
    }

    private async Task SaveFileAsync(FileStorageArea area, string fileName)
    {
        await _storage.SaveAsync(area, fileName, new MemoryStream([1, 2]), 10);
    }

    private void SetOld(string area, string fileName) =>
        File.SetLastWriteTimeUtc(
            Path.Combine(_rootPath, area, fileName),
            DateTime.UtcNow.AddDays(-31));

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
            Directory.Delete(_rootPath, recursive: true);
    }
}
