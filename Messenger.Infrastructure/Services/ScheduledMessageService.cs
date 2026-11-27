using System.Text.Json;
using Messenger.Core.DTOs.Messages;
using Messenger.Application.DTOs.Messages;
using Messenger.Application.Interfaces;
using Messenger.Core.Messages;
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

    public async Task<List<ScheduledMessageDispatchDto>> GetDueAsync(DateTime nowUtc, int limit = 50, CancellationToken ct = default)
    {
        var horizon = nowUtc.AddSeconds(2);
        var entities = await _context.ScheduledMessages.AsNoTracking()
            .Where(x => x.Status == ScheduledMessageStatus.Pending && x.ScheduledAt <= horizon)
            .OrderBy(x => x.ScheduledAt).Take(limit).ToListAsync(ct);
        return entities.Select(MapToDispatchDto).ToList();
    }

    public async Task<ScheduledMessageDto?> UpdateAsync(
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
        return await GetScheduledMessageDtoAsync(entity.ScheduledMessageId, ct);
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
        var entity = await _context.ScheduledMessages
            .FirstOrDefaultAsync(x => x.ScheduledMessageId == scheduledMessageId, ct);
        if (entity == null) return;

        entity.Status = ScheduledMessageStatus.Sent;
        entity.ResultMessageId = resultMessageId;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
    }

    public async Task MarkFailedAsync(Guid scheduledMessageId, string error, CancellationToken ct = default)
    {
        var entity = await _context.ScheduledMessages
            .FirstOrDefaultAsync(x => x.ScheduledMessageId == scheduledMessageId, ct);
        if (entity == null) return;

        entity.Status = ScheduledMessageStatus.Failed;
        entity.ErrorMessage = error?.Length > 500 ? error[..500] : error;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
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
