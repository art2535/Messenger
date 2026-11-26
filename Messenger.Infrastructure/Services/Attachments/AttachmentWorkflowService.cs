using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Interfaces.Attachments;

namespace Messenger.Infrastructure.Services.Attachments
{
    public sealed class AttachmentWorkflowService : IAttachmentWorkflowService
    {
        private readonly IAttachmentService _attachmentService;

        public AttachmentWorkflowService(IAttachmentService attachmentService)
        {
            _attachmentService = attachmentService;
        }

        public Task AddAttachmentAsync(
            Attachment attachment,
            CancellationToken cancellationToken = default)
        {
            return _attachmentService.AddAttachmentAsync(attachment, cancellationToken);
        }

        public Task<IEnumerable<Attachment>> GetAttachmentsByMessageIdAsync(
            Guid messageId,
            CancellationToken cancellationToken = default)
        {
            return _attachmentService.GetAttachmentsByMessageIdAsync(messageId, cancellationToken);
        }
    }
}
