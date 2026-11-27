using System.Text.Json;
using Messenger.Application.DTOs.Messages;
using Messenger.Application.Interfaces;
using Messenger.Application.Messages;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Services;

public class ScheduledMessageService : IScheduledMessageService
{
    private readonly GuapMessengerContext _context;
    private readonly IEncryptionService _encryption;
    private readonly ILogger<ScheduledMessageService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ScheduledMessageService(GuapMessengerContext context, IEncryptionService encryption,
        ILogger<ScheduledMessageService> logger)
    {
        _context = context;
        _encryption = encryption;
        _logger = logger;
    }

    public async Task<ScheduledMessageDto> CreateAsync(Guid chatId, Guid senderId, string? encryptedText,
        bool hasAttachments, List<AttachmentInfo>? attachments, DateTime scheduledAtUtc, string? replyToMessageId = null,
        CancellationToken ct = default)
    {
        if (scheduledAtUtc <= DateTime.UtcNow.AddSeconds(30))
            throw new ArgumentException("Время отправки должно быть хотя бы на 30 секунд в будущем");

        var entity = new ScheduledMessage
        {
            ScheduledMessageId = Guid.NewGuid(),
            ChatId = chatId,
            SenderId = senderId,
            MessageText = encryptedText,
            HasAttachments = hasAttachments || (attachments?.Count > 0),
            AttachmentsJson = attachments is { Count: > 0 }
                ? JsonSerializer.Serialize(attachments, JsonOpts)
                : null,
            ScheduledAt = DateTime.SpecifyKind(scheduledAtUtc, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            Status = ScheduledMessageStatus.Pending,
            ReplyToMessageId = replyToMessageId
        };

        _context.ScheduledMessages.Add(entity);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "ScheduledMessage created {Id} chat={ChatId} at={At:o}",
            entity.ScheduledMessageId, chatId, entity.ScheduledAt);

        return await GetScheduledMessageDtoAsync(entity.ScheduledMessageId, ct)
            ?? throw new InvalidOperationException("Не удалось прочитать созданное запланированное сообщение");
    }

    public async Task<ScheduledMessageDispatchDto?> GetByIdAsync(Guid scheduledMessageId, CancellationToken ct = default)
    {
        var entity = await _context.ScheduledMessages.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ScheduledMessageId == scheduledMessageId, ct);
        return entity == null ? null : MapToDispatchDto(entity);
    }

    public async Task<List<ScheduledMessageDto>> GetPendingByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var entities = await _context.ScheduledMessages.AsNoTracking()
            .Include(x => x.Chat)
            .Where(x => x.SenderId == userId && x.Status == ScheduledMessageStatus.Pending)
            .OrderBy(x => x.ScheduledAt).ToListAsync(ct);
        return entities.Select(MapToDto).ToList();
    }

    public async Task<List<ScheduledMessageDto>> GetPendingByChatAsync(Guid chatId, Guid userId, CancellationToken ct = default)
    {
        var entities = await _context.ScheduledMessages.AsNoTracking()
            .Where(x => x.ChatId == chatId && x.SenderId == userId && x.Status == ScheduledMessageStatus.Pending)
            .OrderBy(x => x.ScheduledAt).ToListAsync(ct);
        return entities.Select(MapToDto).ToList();
    }

    /// <summary>
    /// Атомарно выбирает готовые сообщения и фиксирует их публикацию через MassTransit Bus Outbox.
    /// </summary>
    public async Task<List<(ScheduledMessageDispatchDto Message, Guid ResultMessageId)>> DispatchDueAsync(
        Func<ScheduledMessageDispatchDto, CancellationToken, Task<Guid>> publishAsync,
        DateTime nowUtc, int limit = 50, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(publishAsync);
        if (limit <= 0)
            return new List<(ScheduledMessageDispatchDto, Guid)>();

        var dispatched = new List<(ScheduledMessageDispatchDto Message, Guid ResultMessageId)>();
        var horizon = DateTime.SpecifyKind(nowUtc.AddSeconds(2), DateTimeKind.Utc);

        for (var i = 0; i < limit; i++)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);
            var entity = await _context.ScheduledMessages
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM "Scheduled_Messages"
                    WHERE "Status" = {ScheduledMessageStatus.Pending}
                      AND "Scheduled_At" <= {horizon}
                    ORDER BY "Scheduled_At"
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                    """)
                .FirstOrDefaultAsync(ct);

            if (entity == null)
            {
                await transaction.CommitAsync(ct);
                break;
            }

            var dispatchMessage = MapToDispatchDto(entity);
            var resultMessageId = await publishAsync(dispatchMessage, ct);

            entity.Status = ScheduledMessageStatus.Sent;
            entity.ResultMessageId = resultMessageId;
            entity.ErrorMessage = null;
            entity.UpdatedAt = DateTime.UtcNow;

            // Bus Outbox и ScheduledMessage используют один scoped DbContext и одну транзакцию.
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            dispatched.Add((dispatchMessage, resultMessageId));
        }

        return dispatched;
    }

    public async Task<ScheduledMessageDto?> UpdateAsync(
        Guid scheduledMessageId,
        Guid userId,
        string? encryptedText,
        DateTime? scheduledAtUtc,
        CancellationToken ct = default)
    {
        var existing = await _context.ScheduledMessages.AsNoTracking()
            .Where(x => x.ScheduledMessageId == scheduledMessageId)
            .Select(x => new { x.SenderId, x.Status })
            .FirstOrDefaultAsync(ct);

        if (existing == null)
            return null;
        if (existing.SenderId != userId)
            throw new UnauthorizedAccessException("Можно редактировать только свои запланированные сообщения");
        if (existing.Status != ScheduledMessageStatus.Pending)
            throw new InvalidOperationException("Можно редактировать только ожидающие отправки сообщения");

        DateTime? scheduledAt = null;
        if (scheduledAtUtc.HasValue)
        {
            scheduledAt = DateTime.SpecifyKind(scheduledAtUtc.Value, DateTimeKind.Utc);
            if (scheduledAt <= DateTime.UtcNow.AddSeconds(30))
                throw new ArgumentException("Время отправки должно быть хотя бы на 30 секунд в будущем");
        }

        // Условный UPDATE не перезапишет запись, если диспетчер уже перевёл её в Sent.
        var updated = await _context.ScheduledMessages
            .Where(x => x.ScheduledMessageId == scheduledMessageId
                && x.SenderId == userId
                && x.Status == ScheduledMessageStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.MessageText, encryptedText ?? x.MessageText)
                .SetProperty(x => x.ScheduledAt, scheduledAt ?? x.ScheduledAt)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);

        if (updated == 0)
            throw new InvalidOperationException("Можно редактировать только ожидающие отправки сообщения");

        return await GetScheduledMessageDtoAsync(scheduledMessageId, ct);
    }

    public async Task<bool> CancelAsync(Guid scheduledMessageId, Guid userId, CancellationToken ct = default)
    {
        var entity = await _context.ScheduledMessages
            .AsNoTracking()
            .Where(x => x.ScheduledMessageId == scheduledMessageId)
            .Select(x => new { x.SenderId, x.Status })
            .FirstOrDefaultAsync(ct);

        if (entity == null)
            return false;

        if (entity.SenderId != userId)
            throw new UnauthorizedAccessException("Можно отменять только свои запланированные сообщения");

        if (entity.Status != ScheduledMessageStatus.Pending)
            return false;

        var updated = await _context.ScheduledMessages
            .Where(x => x.ScheduledMessageId == scheduledMessageId
                && x.SenderId == userId
                && x.Status == ScheduledMessageStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, ScheduledMessageStatus.Cancelled)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);

        return updated > 0;
    }

    private async Task<ScheduledMessageDto?> GetScheduledMessageDtoAsync(Guid id, CancellationToken ct)
    {
        var entity = await _context.ScheduledMessages.AsNoTracking()
            .Include(x => x.Sender).Include(x => x.Chat)
            .FirstOrDefaultAsync(x => x.ScheduledMessageId == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    private static ScheduledMessageDispatchDto MapToDispatchDto(ScheduledMessage entity)
    {
        List<AttachmentInfo> attachments = new();
        if (!string.IsNullOrWhiteSpace(entity.AttachmentsJson))
        {
            try
            {
                attachments = JsonSerializer.Deserialize<List<AttachmentInfo>>(entity.AttachmentsJson, JsonOpts)
                    ?? new List<AttachmentInfo>();
            }
            catch { attachments = new List<AttachmentInfo>(); }
        }

        return new ScheduledMessageDispatchDto
        {
            ScheduledMessageId = entity.ScheduledMessageId,
            ChatId = entity.ChatId,
            SenderId = entity.SenderId,
            EncryptedMessageText = entity.MessageText,
            HasAttachments = entity.HasAttachments,
            Attachments = attachments,
            ScheduledAt = DateTime.SpecifyKind(entity.ScheduledAt, DateTimeKind.Utc),
            Status = entity.Status,
            ReplyToMessageId = entity.ReplyToMessageId
        };
    }

    private ScheduledMessageDto MapToDto(ScheduledMessage entity)
    {
        var attachments = new List<AttachmentDto>();
        if (!string.IsNullOrEmpty(entity.AttachmentsJson))
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<AttachmentInfo>>(entity.AttachmentsJson, JsonOpts);
                if (list != null)
                {
                    attachments = list.Select(a => new AttachmentDto
                    {
                        AttachmentId = a.AttachmentId,
                        FileName = a.FileName,
                        FileType = a.FileType,
                        SizeInBytes = (int)a.SizeInBytes,
                        Url = a.Url
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize attachments for scheduled {Id}", entity.ScheduledMessageId);
            }
        }

        var text = string.IsNullOrEmpty(entity.MessageText) ? null : _encryption.TryDecryptSafe(entity.MessageText);

        return new ScheduledMessageDto
        {
            ScheduledMessageId = entity.ScheduledMessageId,
            ChatId = entity.ChatId,
            SenderId = entity.SenderId,
            SenderName = entity.Sender != null
                ? $"{entity.Sender.FirstName} {entity.Sender.LastName}".Trim()
                : null,
            MessageText = text,
            HasAttachments = entity.HasAttachments,
            Attachments = attachments,
            ScheduledAt = DateTime.SpecifyKind(entity.ScheduledAt, DateTimeKind.Utc),
            CreatedAt = DateTime.SpecifyKind(entity.CreatedAt, DateTimeKind.Utc),
            UpdatedAt = entity.UpdatedAt.HasValue
                ? DateTime.SpecifyKind(entity.UpdatedAt.Value, DateTimeKind.Utc)
                : null,
            Status = entity.Status,
            ResultMessageId = entity.ResultMessageId,
            ErrorMessage = entity.ErrorMessage,
            ReplyToMessageId = entity.ReplyToMessageId,
            ChatName = entity.Chat?.Name
        };
    }
}
