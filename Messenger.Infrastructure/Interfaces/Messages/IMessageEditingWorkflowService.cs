using Messenger.Core.DTOs.Messages;

namespace Messenger.Infrastructure.Interfaces.Messages
{
    public interface IMessageEditingWorkflowService
    {
        Task<MessageEditResult> EditAsync(
            Guid chatId,
            Guid messageId,
            Guid userId,
            string text,
            CancellationToken cancellationToken = default);
    }

    public sealed record MessageEditResult(MessageEditStatus Status);

    public enum MessageEditStatus
    {
        Updated,
        NotFound,
        Forbidden
    }
}
