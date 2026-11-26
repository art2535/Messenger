using Messenger.Core.DTOs.Messages;

namespace Messenger.Infrastructure.Services.Messages
{
    public sealed class MessageExportFacade
    {
        private readonly IMessageChatExportService _messageChatExportService;

        public MessageExportFacade(IMessageChatExportService messageChatExportService)
        {
            _messageChatExportService = messageChatExportService;
        }

        public Task<ChatExportResult> ExportChatAsync(
            Guid chatId,
            string format,
            string? chatName = null,
            CancellationToken token = default)
        {
            return _messageChatExportService.ExportChatAsync(chatId, format, chatName, token);
        }
    }
}
