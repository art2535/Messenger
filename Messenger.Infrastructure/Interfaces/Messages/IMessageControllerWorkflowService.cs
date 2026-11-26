using Messenger.Core.Interfaces;
using Messenger.Core.Models;

namespace Messenger.Infrastructure.Services
{
    public interface IMessageControllerWorkflowService
    {
        Task<BulkMessageDeletionResult> DeleteManyAsync(
            Guid chatId,
            IEnumerable<Guid> messageIds,
            CancellationToken cancellationToken = default);

        Task<ChatReadResult> MarkChatAsReadAsync(
            Guid chatId,
            Guid userId,
            CancellationToken cancellationToken = default);

        Task<PinnedMessageResult> SetPinnedAsync(
            Guid messageId,
            Guid userId,
            bool isPinned,
            CancellationToken cancellationToken = default);

        Task<PinnedMessagesResult> GetPinnedAsync(
            Guid chatId,
            Guid userId,
            CancellationToken cancellationToken = default);
    }

    public enum MessageWorkflowStatus
    {
        Success,
        NotFound,
        Forbidden
    }

    public sealed record BulkMessageDeletionResult(
        MessageWorkflowStatus Status,
        int DeletedCount,
        IReadOnlyList<Guid> MessageIds);

    public sealed record ChatReadResult(
        MessageWorkflowStatus Status,
        int UpdatedCount);

    public sealed record PinnedMessageResult(
        MessageWorkflowStatus Status,
        object? Payload,
        Guid ChatId);

    public sealed record PinnedMessageDto(
        Guid MessageId,
        Guid ChatId,
        string Text,
        DateTime? PinnedAt,
        long SequenceNumber,
        Guid SenderId,
        string? SenderName);

    public sealed record PinnedMessagesResult(
        MessageWorkflowStatus Status,
        IReadOnlyList<PinnedMessageDto> Messages);
}
