using Messenger.Application.DTOs.Attachments;
using Messenger.Core.DTOs.Messages;

namespace Messenger.Application.Interfaces;

public interface IAttachmentService
{
    Task AddAttachmentAsync(Guid messageId, CreateAttachmentDto attachment, CancellationToken cancellationToken = default);
    Task<IEnumerable<AttachmentDto>> GetAttachmentsByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default);
}
