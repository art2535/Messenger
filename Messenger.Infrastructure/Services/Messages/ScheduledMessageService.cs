using System.Text.Json;
using Messenger.Core.DTOs.Messages;
using Messenger.Core.Interfaces;
using Messenger.Core.Messages;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Services.Messages;

public class ScheduledMessageService : IScheduledMessageService, IScheduledMessageClaimService
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

    public async Task<ScheduledMessage> CreateAsync(Guid chatId, Guid senderId, string? encryptedText,
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

        return entity;
    }

    public async Task<ScheduledMessage?> GetByIdAsync(Guid scheduledMessageId, CancellationToken ct = default)
    {
        return await _context.ScheduledMessages
            .AsNoTracking()
            .Include(x => x.Sender)
            .Include(x => x.Chat)
            .FirstOrDefaultAsync(x => x.ScheduledMessageId == scheduledMessageId, ct);
    }

    public async Task<List<ScheduledMessage>> GetPendingByUserAsync(Guid userId, CancellationToken ct = default)
    {
        return await _context.ScheduledMessages
            .AsNoTracking()
            .Include(x => x.Chat)
            .Where(x => x.SenderId == userId && x.Status == ScheduledMessageStatus.Pending)
            .OrderBy(x => x.ScheduledAt)
            .ToListAsync(ct);
    }

    public async Task<List<ScheduledMessage>> GetPendingByChatAsync(Guid chatId, Guid userId, CancellationToken ct = default)
    {
        return await _context.ScheduledMessages
            .AsNoTracking()
            .Where(x => x.ChatId == chatId
                        && x.SenderId == userId
                        && x.Status == ScheduledMessageStatus.Pending)
            .OrderBy(x => x.ScheduledAt)
            .ToListAsync(ct);
    }

    public async Task<List<ScheduledMessage>> GetDueAsync(DateTime nowUtc, int limit = 50, CancellationToken ct = default)
    {
        // Возвращаем в обработку сообщения, захват которых завис после сбоя экземпляра API.
        var horizon = nowUtc.AddSeconds(2);
        var leaseExpiredAt = nowUtc.AddMinutes(-5);

        return await _context.ScheduledMessages
            .AsNoTracking()
            .Where(x =>
                x.ScheduledAt <= horizon
                && (x.Status == ScheduledMessageStatus.Pending
                    || (x.Status == ScheduledMessageStatus.Processing
                        && x.UpdatedAt <= leaseExpiredAt)))
            .OrderBy(x => x.ScheduledAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<bool> TryClaimAsync(
        Guid scheduledMessageId,
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        var utcNow = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        var horizon = utcNow.AddSeconds(2);
        var leaseExpiredAt = utcNow.AddMinutes(-5);

        var affectedRows = await _context.ScheduledMessages
            .Where(x =>
                x.ScheduledMessageId == scheduledMessageId
                && x.ScheduledAt <= horizon
                && (x.Status == ScheduledMessageStatus.Pending
                    || (x.Status == ScheduledMessageStatus.Processing
                        && x.UpdatedAt <= leaseExpiredAt)))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, ScheduledMessageStatus.Processing)
                    .SetProperty(x => x.UpdatedAt, utcNow),
                ct);

        return affectedRows == 1;
    }

    public async Task<ScheduledMessage?> UpdateAsync(
        Guid scheduledMessageId,
        Guid userId,
        string? encryptedText,
        DateTime? scheduledAtUtc,
        CancellationToken ct = default)
    {
        var entity = await _context.ScheduledMessages
            .FirstOrDefaultAsync(x => x.ScheduledMessageId == scheduledMessageId, ct);

        if (entity == null)
            return null;

        if (entity.SenderId != userId)
            throw new UnauthorizedAccessException("Можно редактировать только свои запланированные сообщения");

        if (entity.Status != ScheduledMessageStatus.Pending)
            throw new InvalidOperationException("Можно редактировать только ожидающие отправки сообщения");

        if (encryptedText != null)
            entity.MessageText = encryptedText;

        if (scheduledAtUtc.HasValue)
        {
            var at = DateTime.SpecifyKind(scheduledAtUtc.Value, DateTimeKind.Utc);
            if (at <= DateTime.UtcNow.AddSeconds(30))
                throw new ArgumentException("Время отправки должно быть хотя бы на 30 секунд в будущем");
            entity.ScheduledAt = at;
        }

        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<bool> CancelAsync(Guid scheduledMessageId, Guid userId, CancellationToken ct = default)
    {
        var entity = await _context.ScheduledMessages
            .FirstOrDefaultAsync(x => x.ScheduledMessageId == scheduledMessageId, ct);

        if (entity == null)
            return false;

        if (entity.SenderId != userId)
            throw new UnauthorizedAccessException("Можно отменять только свои запланированные сообщения");

        if (entity.Status != ScheduledMessageStatus.Pending)
            return false;

        entity.Status = ScheduledMessageStatus.Cancelled;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task MarkSentAsync(Guid scheduledMessageId, Guid resultMessageId, CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        await _context.ScheduledMessages
            .Where(x =>
                x.ScheduledMessageId == scheduledMessageId
                && x.Status == ScheduledMessageStatus.Processing)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, ScheduledMessageStatus.Sent)
                    .SetProperty(x => x.ResultMessageId, resultMessageId)
                    .SetProperty(x => x.UpdatedAt, nowUtc),
                ct);
    }

    public async Task MarkFailedAsync(Guid scheduledMessageId, string error, CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        var safeError = error?.Length > 500 ? error[..500] : error;

        await _context.ScheduledMessages
            .Where(x =>
                x.ScheduledMessageId == scheduledMessageId
                && x.Status == ScheduledMessageStatus.Processing)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, ScheduledMessageStatus.Failed)
                    .SetProperty(x => x.ErrorMessage, safeError)
                    .SetProperty(x => x.UpdatedAt, nowUtc),
                ct);
    }

    public ScheduledMessageDto ToDto(ScheduledMessage entity, string? decryptedText = null,
        string? senderName = null, string? chatName = null)
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

        string? text = decryptedText;
        if (text == null && !string.IsNullOrEmpty(entity.MessageText))
            text = _encryption.TryDecryptSafe(entity.MessageText);

        return new ScheduledMessageDto
        {
            ScheduledMessageId = entity.ScheduledMessageId,
            ChatId = entity.ChatId,
            SenderId = entity.SenderId,
            SenderName = senderName
                         ?? (entity.Sender != null
                             ? $"{entity.Sender.FirstName} {entity.Sender.LastName}".Trim()
                             : null),
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
            ChatName = chatName ?? entity.Chat?.Name
        };
    }
}
