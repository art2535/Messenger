namespace Messenger.Infrastructure.Interfaces.Messages
{
    public interface IMessageDeletionWorkflowService
    {
        Task<MessageDeletionStatus> DeleteAsync(
            Guid chatId,
            Guid messageId,
            Guid userId,
            CancellationToken cancellationToken = default);
    }

    public enum MessageDeletionStatus
    {
        Deleted,
        NotFound,
        Forbidden
    }
}
