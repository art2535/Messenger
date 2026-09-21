using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Services
{
    public class AttachmentService : IAttachmentService
    {
        private readonly GuapMessengerContext _context;

        public AttachmentService(GuapMessengerContext context)
        {
            _context = context;
        }

        public async Task AddAttachmentAsync(Attachment attachment, CancellationToken cancellationToken = default)
        {
            await _context.Attachments.AddAsync(attachment, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<IEnumerable<Attachment>> GetAttachmentsByMessageIdAsync(Guid messageId,
            CancellationToken cancellationToken = default)
        {
            return await _context.Attachments
                .Where(a => a.MessageId == messageId)
                .ToListAsync(cancellationToken);
        }
    }
}
