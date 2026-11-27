using Messenger.Application.DTOs.Attachments;
using Messenger.Application.DTOs.Messages;

namespace Messenger.Application.Interfaces;

public interface IAttachmentService
{
    Task AddAttachmentAsync(Guid messageId, CreateAttachmentDto attachment, CancellationToken cancellationToken = default);
    Task AddAttachmentAsync(Guid messageId, Guid attachmentId, CreateAttachmentDto attachment, CancellationToken cancellationToken = default);
    Task<IEnumerable<AttachmentDto>> GetAttachmentsByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default);
    Task<(AttachmentDto? Attachment, Guid? MessageId)> GetAttachmentWithMessageIdAsync(Guid attachmentId, CancellationToken cancellationToken = default);
}
