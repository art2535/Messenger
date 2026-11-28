using Messenger.Application.Files;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text;
using System.Text.Json;

namespace Messenger.API.Services;

/// <summary>
/// Периодически удаляет старые файлы, на которые больше не ссылаются записи вложений или аватаров.
/// </summary>
public sealed class UploadOrphanCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan OrphanGracePeriod = TimeSpan.FromDays(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<UploadOrphanCleanupService> _logger;

    public UploadOrphanCleanupService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IFileStorage fileStorage,
        ILogger<UploadOrphanCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var nextDelay = CleanupInterval;
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при очистке неиспользуемых файлов загрузок");
                nextDelay = TimeSpan.FromMinutes(10);
            }

            try
            {
                await Task.Delay(nextDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>Запускает один цикл очистки; используется для управляемого запуска и интеграционных тестов.</summary>
    public Task CleanupNowAsync(CancellationToken cancellationToken = default) =>
        CleanupAsync(cancellationToken);

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(_configuration["URL:API:HTTPS"], UriKind.Absolute, out _))
        {
            _logger.LogWarning("URL:API:HTTPS не задан; очистка файлов пропущена");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GuapMessengerContext>();

        var attachmentUrls = await context.Attachments
            .AsNoTracking()
            .Select(attachment => attachment.Url)
            .ToListAsync(cancellationToken);

        var avatarUrls = await context.AccountSettings
            .AsNoTracking()
            .Where(account => account.Avatar != null)
            .Select(account => account.Avatar!)
            .ToListAsync(cancellationToken);

        var scheduledAttachmentPayloads = await context.ScheduledMessages
            .AsNoTracking()
            .Where(message => message.Status == ScheduledMessageStatus.Pending
                && message.AttachmentsJson != null)
            .Select(message => message.AttachmentsJson!)
            .ToListAsync(cancellationToken);

        // Сообщение может находиться в transactional outbox до создания Attachment.
        var outboxBodies = await GetOutboxBodiesAsync(context, cancellationToken);
        var pendingUrls = new List<string?>();
        foreach (var body in outboxBodies.Concat(scheduledAttachmentPayloads))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                CollectStringValues(document.RootElement, pendingUrls);
            }
            catch (JsonException)
            {
                // Неизвестный payload делает безопасную очистку невозможной.
                _logger.LogWarning("Не удалось разобрать payload очереди или запланированного сообщения; очистка пропущена");
                return;
            }
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var referencedUploads = CollectReferencedFileNames(
            attachmentUrls.Concat(pendingUrls), "uploads/", comparison);
        var referencedAvatars = CollectReferencedFileNames(avatarUrls, "avatars/", comparison);

        var deletedUploads = await CleanupDirectoryAsync(
            FileStorageArea.Uploads, referencedUploads, cancellationToken);
        var deletedAvatars = await CleanupDirectoryAsync(
            FileStorageArea.Avatars, referencedAvatars, cancellationToken);

        if (deletedUploads > 0 || deletedAvatars > 0)
        {
            _logger.LogInformation(
                "Удалены неиспользуемые файлы: вложений {AttachmentCount}, аватаров {AvatarCount}",
                deletedUploads,
                deletedAvatars);
        }
    }

    private static async Task<List<string>> GetOutboxBodiesAsync(
        GuapMessengerContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """SELECT "Body" FROM "OutboxMessage" WHERE "OutboxId" IS NOT NULL AND "MessageType" LIKE '%ChatMessageSent%'""";

            var bodies = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(0))
                    continue;

                var bodyValue = reader.GetValue(0);
                if (bodyValue is string body)
                    bodies.Add(body);
                else if (bodyValue is byte[] bodyBytes)
                    bodies.Add(Encoding.UTF8.GetString(bodyBytes));
            }

            return bodies;
        }
        finally
        {
            if (closeConnection)
                await connection.CloseAsync();
        }
    }

    private static void CollectStringValues(JsonElement element, ICollection<string?> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                values.Add(element.GetString());
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectStringValues(item, values);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    CollectStringValues(property.Value, values);
                break;
        }
    }

    private static HashSet<string> CollectReferencedFileNames(
        IEnumerable<string?> urls,
        string expectedPrefix,
        StringComparer comparer)
    {
        var fileNames = new HashSet<string>(comparer);

        foreach (var value in urls)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            string path;
            if (Uri.TryCreate(value, UriKind.Absolute, out var fileUri))
            {
                // Любой абсолютный URL с локальным uploads/avatars path сохраняет файл.
                // Лишняя ссылка безопаснее преждевременного удаления файла.
                path = fileUri.AbsolutePath;
            }
            else
            {
                path = value.Split('?')[0];
            }

            try
            {
                path = Uri.UnescapeDataString(path).Replace('\\', '/').TrimStart('/');
            }
            catch (UriFormatException)
            {
                continue;
            }

            if (!path.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var fileName = path[expectedPrefix.Length..];
            if (string.IsNullOrWhiteSpace(fileName)
                || fileName.Contains('/')
                || fileName.Contains('\\')
                || fileName is "." or "..")
                continue;

            fileNames.Add(fileName);
        }

        return fileNames;
    }

    private async Task<int> CleanupDirectoryAsync(
        FileStorageArea area,
        HashSet<string> referencedFileNames,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - OrphanGracePeriod;
        var deletedCount = 0;
        var files = await _fileStorage.ListAsync(area, cancellationToken);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (referencedFileNames.Contains(file.FileName) || file.LastModifiedUtc > cutoff)
                continue;

            try
            {
                await _fileStorage.DeleteAsync(area, file.FileName, cancellationToken);
                deletedCount++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Не удалось удалить неиспользуемый файл {FileName}", file.FileName);
            }
        }

        return deletedCount;
    }
}
