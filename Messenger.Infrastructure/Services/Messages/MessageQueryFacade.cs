using Messenger.Core.DTOs.Messages;
using Messenger.Core.Models;

namespace Messenger.Infrastructure.Services.Messages
{
    public sealed class MessageQueryFacade
    {
        private readonly IMessagePaginationService _paginationService;
        private readonly IMessageLookupService _lookupService;
        private readonly IMessageSearchService _searchService;
        private readonly IMessagePinningService _pinningService;

        public MessageQueryFacade(
            IMessagePaginationService paginationService,
            IMessageLookupService lookupService,
            IMessageSearchService searchService,
            IMessagePinningService pinningService)
        {
            _paginationService = paginationService;
            _lookupService = lookupService;
            _searchService = searchService;
            _pinningService = pinningService;
        }

        public async Task<IEnumerable<Message>> GetMessagesAsync(
            Guid chatId,
            CancellationToken token = default)
        {
            var (items, _) = await _paginationService.GetMessagesByChatIdPagedAsync(
                chatId,
                null,
                100,
                token);
            return items;
        }

        public Task<(IReadOnlyList<Message> Items, bool HasMore)> GetMessagesPagedAsync(
            Guid chatId,
            long? beforeSequence = null,
            int limit = 50,
            CancellationToken token = default)
        {
            return _paginationService.GetMessagesByChatIdPagedAsync(
                chatId,
                beforeSequence,
                limit,
                token);
        }

        public Task<Message?> GetMessageByIdAsync(
            Guid chatId,
            Guid messageId,
            CancellationToken token = default)
        {
            return _lookupService.GetMessageByIdAsync(chatId, messageId, token);
        }

        public Task<Message?> GetMessageByIdGlobalAsync(
            Guid messageId,
            CancellationToken token = default)
        {
            return _lookupService.GetMessageByIdGlobalAsync(messageId, token);
        }

        public Task<List<MessageDto>> SearchMessagesAsync(
            Guid chatId,
            string query,
            CancellationToken token = default)
        {
            return _searchService.SearchMessagesAsync(chatId, query, token);
        }

        public Task<List<Message>> GetPinnedMessagesAsync(
            Guid chatId,
            CancellationToken token = default)
        {
            return _pinningService.GetPinnedMessagesAsync(chatId, token);
        }
    }
}
