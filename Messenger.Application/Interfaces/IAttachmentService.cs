using Messenger.Core.DTOs.Attachments;
using Messenger.Core.DTOs.Messages;

namespace Messenger.Core.Interfaces;

public interface IAttachmentService
{
    Task AddAttachmentAsync(Guid messageId, CreateAttachmentDto attachment, CancellationToken cancellationToken = default);
    Task<IEnumerable<AttachmentDto>> GetAttachmentsByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default);
}
