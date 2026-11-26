using Messenger.Core.DTOs.Messages;

namespace Messenger.Infrastructure.Interfaces.Messages
{
    public interface IMessageChatExportService
    {
        Task<ChatExportResult> ExportChatAsync(
            Guid chatId,
            string format,
            string? chatName = null,
            CancellationToken token = default);
    }
}
