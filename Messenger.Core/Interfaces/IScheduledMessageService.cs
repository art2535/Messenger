using Messenger.Core.DTOs.Messages;
using Messenger.Core.Messages;
using Messenger.Core.Models;

namespace Messenger.Core.Interfaces;

public interface IScheduledMessageService
{
    Task<ScheduledMessage> CreateAsync(
        Guid chatId,
        Guid senderId,
        string? encryptedText,
        bool hasAttachments,
        List<AttachmentInfo>? attachments,
        DateTime scheduledAtUtc,
        string? replyToMessageId = null,
        CancellationToken ct = default);

    Task<ScheduledMessage?> GetByIdAsync(Guid scheduledMessageId, CancellationToken ct = default);

    Task<List<ScheduledMessage>> GetPendingByUserAsync(Guid userId, CancellationToken ct = default);

    Task<List<ScheduledMessage>> GetPendingByChatAsync(Guid chatId, Guid userId, CancellationToken ct = default);

    Task<List<ScheduledMessage>> GetDueAsync(DateTime nowUtc, int limit = 50, CancellationToken ct = default);

    Task<bool> TryClaimAsync(
        Guid scheduledMessageId,
        DateTime nowUtc,
        CancellationToken ct = default);

    Task<ScheduledMessage?> UpdateAsync(
        Guid scheduledMessageId,
        Guid userId,
        string? encryptedText,
        DateTime? scheduledAtUtc,
        CancellationToken ct = default);

    Task<bool> CancelAsync(Guid scheduledMessageId, Guid userId, CancellationToken ct = default);

    Task MarkSentAsync(Guid scheduledMessageId, Guid resultMessageId, CancellationToken ct = default);

    Task MarkFailedAsync(Guid scheduledMessageId, string error, CancellationToken ct = default);

    ScheduledMessageDto ToDto(ScheduledMessage entity, string? decryptedText = null, string? senderName = null, string? chatName = null);
}
