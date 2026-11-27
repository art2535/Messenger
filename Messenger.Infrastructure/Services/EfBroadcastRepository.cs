using Messenger.Application.DTOs.Broadcasts;
using Messenger.Application.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Services;

/// <summary>
/// EF Core-реализация доступа к данным рассылок.
/// </summary>
public sealed class EfBroadcastRepository : IBroadcastRepository
{
    private readonly GuapMessengerContext _context;

    public EfBroadcastRepository(GuapMessengerContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Guid>> GetExistingRecipientIdsAsync(
        IReadOnlyCollection<Guid> recipientIds,
        CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AsNoTracking()
            .Where(user => recipientIds.Contains(user.UserId))
            .Select(user => user.UserId)
            .ToListAsync(cancellationToken);
    }

    public async Task<BroadcastCreatedResponse> CreateBroadcastAsync(
        CreateBroadcastRequest request,
        Guid senderId,
        IReadOnlyCollection<Guid> recipientIds,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var broadcast = new Broadcast
        {
            BroadcastId = Guid.NewGuid(),
            Title = request.Title,
            MessageText = request.MessageText,
            SenderId = senderId,
            CreatedAt = now,
            TotalRecipients = recipientIds.Count
        };

        var recipients = recipientIds.Select(userId => new BroadcastRecipient
        {
            BroadcastId = broadcast.BroadcastId,
            UserId = userId,
            SentAt = now
        }).ToList();

        // Один SaveChanges выполняет обе вставки в одной транзакции EF Core.
        _context.Broadcasts.Add(broadcast);
        _context.BroadcastRecipients.AddRange(recipients);
        await _context.SaveChangesAsync(cancellationToken);

        return new BroadcastCreatedResponse
        {
            BroadcastId = broadcast.BroadcastId,
            TotalRecipients = broadcast.TotalRecipients,
            CreatedAt = broadcast.CreatedAt
        };
    }

    public async Task<BroadcastSummaryDto?> GetBroadcastSummaryAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var broadcast = await _context.Broadcasts
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.BroadcastId == id, cancellationToken);

        if (broadcast is null)
            return null;

        var recipients = await _context.BroadcastRecipients
            .AsNoTracking()
            .Where(recipient => recipient.BroadcastId == id)
            .Select(recipient => new RecipientStatusDto
            {
                UserId = recipient.UserId,
                IsRead = recipient.IsRead,
                ReadAt = recipient.ReadAt
            })
            .ToListAsync(cancellationToken);

        var readCount = await _context.BroadcastRecipients
            .AsNoTracking()
            .CountAsync(recipient => recipient.BroadcastId == id && recipient.IsRead, cancellationToken);

        return new BroadcastSummaryDto
        {
            BroadcastId = broadcast.BroadcastId,
            Title = broadcast.Title,
            MessageText = broadcast.MessageText,
            SenderId = broadcast.SenderId,
            CreatedAt = broadcast.CreatedAt,
            TotalRecipients = broadcast.TotalRecipients,
            ReadCount = readCount,
            Recipients = recipients
        };
    }

    public async Task<MarkAsReadResponse?> MarkAsReadAsync(
        Guid broadcastId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var recipient = await _context.BroadcastRecipients
            .FirstOrDefaultAsync(
                item => item.BroadcastId == broadcastId && item.UserId == userId,
                cancellationToken);

        if (recipient is null)
            return null;

        if (!recipient.IsRead)
        {
            recipient.IsRead = true;
            recipient.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new MarkAsReadResponse
        {
            Success = true,
            ReadAt = recipient.ReadAt
        };
    }

    public async Task<List<BroadcastInboxItemDto>> GetMyBroadcastsAsync(
        Guid userId,
        bool unreadOnly = true,
        CancellationToken cancellationToken = default)
    {
        var query = _context.BroadcastRecipients
            .AsNoTracking()
            .Where(recipient => recipient.UserId == userId);

        if (unreadOnly)
            query = query.Where(recipient => !recipient.IsRead);

        return await query
            .OrderByDescending(recipient => recipient.Broadcast.CreatedAt)
            .Select(recipient => new BroadcastInboxItemDto
            {
                BroadcastId = recipient.Broadcast.BroadcastId,
                Title = recipient.Broadcast.Title,
                MessageText = recipient.Broadcast.MessageText,
                CreatedAt = recipient.Broadcast.CreatedAt,
                IsRead = recipient.IsRead,
                ReadAt = recipient.ReadAt
            })
            .ToListAsync(cancellationToken);
    }
}
