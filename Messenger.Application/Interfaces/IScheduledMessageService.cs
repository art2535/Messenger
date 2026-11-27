using Messenger.Application.DTOs.Messages;
using Messenger.Application.Messages;

namespace Messenger.Application.Interfaces;

public interface IScheduledMessageService
{
    Task<ScheduledMessageDto> CreateAsync(Guid chatId, Guid senderId, string? encryptedText,
        bool hasAttachments, List<AttachmentInfo>? attachments, DateTime scheduledAtUtc,
        string? replyToMessageId = null, CancellationToken ct = default);
    Task<ScheduledMessageDispatchDto?> GetByIdAsync(Guid scheduledMessageId, CancellationToken ct = default);
    Task<List<ScheduledMessageDto>> GetPendingByUserAsync(Guid userId, CancellationToken ct = default);
    Task<List<ScheduledMessageDto>> GetPendingByChatAsync(Guid chatId, Guid userId, CancellationToken ct = default);
    Task<List<ScheduledMessageDispatchDto>> GetDueAsync(DateTime nowUtc, int limit = 50, CancellationToken ct = default);
    Task<ScheduledMessageDto?> UpdateAsync(Guid scheduledMessageId, Guid userId, string? encryptedText,
        DateTime? scheduledAtUtc, CancellationToken ct = default);
    Task<bool> CancelAsync(Guid scheduledMessageId, Guid userId, CancellationToken ct = default);
    Task MarkSentAsync(Guid scheduledMessageId, Guid resultMessageId, CancellationToken ct = default);
    Task MarkFailedAsync(Guid scheduledMessageId, string error, CancellationToken ct = default);
}
