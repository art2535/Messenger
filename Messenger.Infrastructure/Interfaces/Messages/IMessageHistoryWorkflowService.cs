using Messenger.Core.DTOs.Messages;

namespace Messenger.Infrastructure.Interfaces.Messages
{
    public interface IMessageHistoryWorkflowService
    {
        Task<MessageHistoryResult> GetMessagesAsync(
            Guid chatId,
            long? beforeSequence,
            int limit,
            CancellationToken cancellationToken = default);

        Task<MessageHistoryExportResult> ExportChatAsync(
            Guid chatId,
            Guid userId,
            string? format,
            CancellationToken cancellationToken = default);
    }

    public enum MessageHistoryStatus
    {
        Success,
        Forbidden,
        NotFound,
        InvalidFormat
    }

    public sealed record MessageHistoryResult(
        IReadOnlyList<MessageDto> Messages,
        bool HasMore,
        long? NextBeforeSequence);

    public sealed record MessageHistoryExportResult(
        MessageHistoryStatus Status,
        ChatExportResult? Export);
}
