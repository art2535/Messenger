using Messenger.Application.DTOs.Attachments;
using Messenger.Application.DTOs.Messages;
using Messenger.Application.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Services;

public class AttachmentService : IAttachmentService
{
    private readonly GuapMessengerContext _context;

    public AttachmentService(GuapMessengerContext context)
    {
        _context = context;
    }

    public async Task AddAttachmentAsync(Guid messageId, CreateAttachmentDto attachment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        var entity = new Attachment
        {
            AttachmentId = Guid.NewGuid(),
            MessageId = messageId,
            FileName = attachment.FileName,
            FileType = attachment.FileType,
            SizeInBytes = attachment.SizeInBytes,
            Url = attachment.Url
        };

        await _context.Attachments.AddAsync(entity, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<AttachmentDto>> GetAttachmentsByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        return await _context.Attachments
            .AsNoTracking()
            .Where(a => a.MessageId == messageId)
            .Select(a => new AttachmentDto
            {
                AttachmentId = a.AttachmentId,
                FileName = a.FileName,
                FileType = a.FileType ?? string.Empty,
                SizeInBytes = a.SizeInBytes ?? 0,
                Url = a.Url
            })
            .ToListAsync(cancellationToken);
    }
}
