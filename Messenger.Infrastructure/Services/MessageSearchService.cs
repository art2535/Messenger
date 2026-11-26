using Messenger.Core.DTOs.Messages;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Services
{
    public sealed class MessageSearchService : IMessageSearchService
    {
        private readonly GuapMessengerContext _context;
        private readonly IEncryptionService _encryptionService;

        private const int SearchScanLimit = 300;

        public MessageSearchService(
            GuapMessengerContext context,
            IEncryptionService encryptionService)
        {
            _context = context;
            _encryptionService = encryptionService;
        }

        public async Task<List<MessageDto>> SearchMessagesAsync(Guid chatId, string query, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<MessageDto>();

            query = query.Trim().ToLowerInvariant();

            var messages = await _context.Messages
                .AsNoTracking()
                .Where(m => m.ChatId == chatId)
                .OrderByDescending(m => m.SequenceNumber)
                .Take(SearchScanLimit)
                .Include(m => m.Sender)
                .Include(m => m.Reactions)
                    .ThenInclude(r => r.User)
                .Include(m => m.Attachments)
                .AsSplitQuery()
                .ToListAsync(token);

            messages.Reverse();

            var filtered = new List<MessageDto>();

            foreach (var m in messages)
            {
                string decryptedText = _encryptionService.TryDecryptSafe(m.MessageText);

                if (decryptedText.ToLowerInvariant().Contains(query))
                {
                    filtered.Add(MapToDto(m, decryptedText));
                }
            }

            return filtered.OrderBy(m => m.SentAt).ToList();
        }


        private static MessageDto MapToDto(Message m, string decryptedText)
        {
            var reactionGroups = (m.Reactions ?? Enumerable.Empty<Reaction>())
                .GroupBy(r => r.ReactionType)
                .Select(g => new ReactionSummaryDto
                {
                    ReactionType = g.Key,
                    Count = g.Count(),
                    Users = g.Select(r => new ReactionUserDto
                    {
                        UserId = r.UserId,
                        UserName = r.User != null
                            ? $"{r.User.FirstName} {r.User.LastName}".Trim()
                            : null
                    }).ToList()
                })
                .OrderByDescending(x => x.Count)
                .ToList();

            return new MessageDto
            {
                MessageId = m.MessageId,
                ChatId = m.ChatId,
                SenderId = m.SenderId,
                SenderName = m.Sender != null
                    ? $"{m.Sender.FirstName} {m.Sender.LastName}".Trim()
                    : "Пользователь",
                MessageText = decryptedText,
                SentAt = Messenger.Core.Models.UtcDateTime.AsUtc(m.SendTime),
                SequenceNumber = m.SequenceNumber,
                Status = m.DeliveryStatus.ToString(),
                Attachments = m.Attachments?.Select(a => new AttachmentDto
                {
                    AttachmentId = a.AttachmentId,
                    FileName = a.FileName,
                    FileType = a.FileType,
                    SizeInBytes = a.SizeInBytes ?? 0,
                    Url = a.Url
                }).ToList() ?? new List<AttachmentDto>(),
                Reactions = reactionGroups,
                IsPinned = m.IsPinned,
                PinnedAt = m.PinnedAt.HasValue
                    ? Messenger.Core.Models.UtcDateTime.AsUtc(m.PinnedAt.Value)
                    : null
            };
        }
    }
}