using Messenger.Core.DTOs.Messages;

namespace Messenger.Infrastructure.Services
{
    public interface IMessageSearchWorkflowService
    {
        Task<List<MessageDto>> SearchAsync(
            Guid chatId,
            string? query,
            CancellationToken cancellationToken = default);
    }
}
