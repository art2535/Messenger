using Messenger.Core.DTOs;

namespace Messenger.Infrastructure.Services
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
