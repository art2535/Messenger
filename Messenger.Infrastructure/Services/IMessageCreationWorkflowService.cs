using Messenger.Core.DTOs;
using Messenger.Core.Models;

namespace Messenger.Infrastructure.Services
{
    public interface IMessageCreationWorkflowService
    {
        Task<ServiceResult<Message>> SendMessageAsync(
            Guid messageId,
            Guid chatId,
            Guid senderId,
            string? content,
            bool hasAttachments,
            CancellationToken token = default);
    }
}
