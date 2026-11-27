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

    public Task AddAttachmentAsync(Guid messageId, CreateAttachmentDto attachment,
        CancellationToken cancellationToken = default)
        => AddAttachmentAsync(messageId, Guid.NewGuid(), attachment, cancellationToken);

    /// <summary>
    /// Идемпотентно добавляет вложение с идентификатором из исходного события.
    /// </summary>
    public async Task AddAttachmentAsync(Guid messageId, Guid attachmentId, CreateAttachmentDto attachment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        if (attachmentId == Guid.Empty)
            throw new ArgumentException("Идентификатор вложения не может быть пустым", nameof(attachmentId));

        var existing = await _context.Attachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.AttachmentId == attachmentId, cancellationToken);
        if (existing != null)
        {
            if (existing.MessageId != messageId || existing.FileName != attachment.FileName
                || existing.FileType != attachment.FileType || existing.SizeInBytes != attachment.SizeInBytes
                || existing.Url != attachment.Url)
                throw new InvalidOperationException($"AttachmentId {attachmentId} уже используется другим вложением");
            return;
        }

        var entity = new Attachment
        {
            AttachmentId = attachmentId,
            MessageId = messageId,
            FileName = attachment.FileName,
            FileType = attachment.FileType,
            SizeInBytes = attachment.SizeInBytes,
            Url = attachment.Url
        };

        await _context.Attachments.AddAsync(entity, cancellationToken);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.Entry(entity).State = EntityState.Detached;
            var concurrent = await _context.Attachments.AsNoTracking()
                .FirstOrDefaultAsync(a => a.AttachmentId == attachmentId, cancellationToken);
            if (concurrent == null || concurrent.MessageId != messageId
                || concurrent.FileName != attachment.FileName || concurrent.FileType != attachment.FileType
                || concurrent.SizeInBytes != attachment.SizeInBytes || concurrent.Url != attachment.Url)
                throw;
        }
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
