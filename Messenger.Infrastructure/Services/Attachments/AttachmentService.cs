using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Services.Attachments
{
    public class AttachmentService : IAttachmentService
    {
        private readonly GuapMessengerContext _context;

        public AttachmentService(GuapMessengerContext context)
        {
            _context = context;
        }

        public async Task AddAttachmentAsync(
            Attachment attachment,
            CancellationToken cancellationToken = default)
        {
            var affectedRows = await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "Attachments"
                    ("Attachment_ID", "Message_ID", "File_Name", "File_Type", "Size_in_Bytes", "URL")
                VALUES
                    ({attachment.AttachmentId}, {attachment.MessageId}, {attachment.FileName},
                     {attachment.FileType}, {attachment.SizeInBytes}, {attachment.Url})
                ON CONFLICT ("Attachment_ID") DO NOTHING
                """,
                cancellationToken);

            if (affectedRows == 1)
                return;

            var existingAttachment = await _context.Attachments
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    a => a.AttachmentId == attachment.AttachmentId,
                    cancellationToken);

            if (existingAttachment is null ||
                existingAttachment.MessageId != attachment.MessageId ||
                existingAttachment.FileName != attachment.FileName ||
                existingAttachment.FileType != attachment.FileType ||
                existingAttachment.SizeInBytes != attachment.SizeInBytes ||
                existingAttachment.Url != attachment.Url)
            {
                throw new InvalidOperationException(
                    "Идентификатор вложения уже используется другими данными");
            }
        }

        public async Task<IEnumerable<Attachment>> GetAttachmentsByMessageIdAsync(Guid messageId,
            CancellationToken cancellationToken = default)
        {
            return await _context.Attachments
                .AsNoTracking()
                .Where(a => a.MessageId == messageId)
                .ToListAsync(cancellationToken);
        }
    }
}
