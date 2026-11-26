using Messenger.Core.Models;

namespace Messenger.Infrastructure.Interfaces.Attachments
{
    public interface IAttachmentWorkflowService
    {
        Task AddAttachmentAsync(
            Attachment attachment,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<Attachment>> GetAttachmentsByMessageIdAsync(
            Guid messageId,
            CancellationToken cancellationToken = default);
    }
}
