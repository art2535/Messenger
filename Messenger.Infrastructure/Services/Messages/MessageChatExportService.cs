using Messenger.Core.DTOs.Messages;
using Messenger.Core.Models;

namespace Messenger.Infrastructure.Services.Messages
{
    public sealed class MessageChatExportService : IMessageChatExportService
    {
        private readonly IMessagePaginationService _messagePaginationService;
        private readonly IMessageExportService _messageExportService;

        public MessageChatExportService(
            IMessagePaginationService messagePaginationService,
            IMessageExportService messageExportService)
        {
            _messagePaginationService = messagePaginationService;
            _messageExportService = messageExportService;
        }

        public async Task<ChatExportResult> ExportChatAsync(
            Guid chatId,
            string format,
            string? chatName = null,
            CancellationToken token = default)
        {
            format = (format ?? "txt").Trim().ToLowerInvariant();
            if (format is not ("txt" or "json" or "html" or "csv"))
            {
                format = "txt";
            }

            var allMessages = new List<Message>();
            long? beforeSeq = null;
            const int pageSize = 200;
            const int maxMessages = 5000;
            var hasMore = true;

            while (hasMore && allMessages.Count < maxMessages)
            {
                var (batch, more) = await _messagePaginationService.GetMessagesByChatIdPagedAsync(
                    chatId,
                    beforeSeq,
                    pageSize,
                    token);

                if (batch.Count == 0)
                {
                    break;
                }

                allMessages.InsertRange(0, batch);
                beforeSeq = batch.Min(message => message.SequenceNumber);
                hasMore = more;

                if (batch.Count < pageSize)
                {
                    hasMore = false;
                }
            }

            allMessages = allMessages
                .OrderBy(message => message.SequenceNumber)
                .ToList();

            return _messageExportService.Export(chatId, format, chatName, allMessages);
        }
    }
}
