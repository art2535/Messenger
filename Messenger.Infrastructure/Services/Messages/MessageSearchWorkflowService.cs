using Messenger.Core.DTOs.Messages;

namespace Messenger.Infrastructure.Services.Messages
{
    public sealed class MessageSearchWorkflowService : IMessageSearchWorkflowService
    {
        private readonly IMessageSearchService _messageSearchService;

        public MessageSearchWorkflowService(IMessageSearchService messageSearchService)
        {
            _messageSearchService = messageSearchService;
        }

        public Task<List<MessageDto>> SearchAsync(
            Guid chatId,
            string? query,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Task.FromResult(new List<MessageDto>());
            }

            return _messageSearchService.SearchMessagesAsync(
                chatId,
                query.Trim(),
                cancellationToken);
        }
    }
}
