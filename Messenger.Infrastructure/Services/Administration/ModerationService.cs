using System.Text.RegularExpressions;
using Messenger.Core.DTOs.Moderation;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Services
{
    public class ModerationService : IModerationService
    {
        private readonly GuapMessengerContext _context;

        private static readonly Regex SuspiciousUrl = new(
            @"https?://[^\s]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".bat", ".cmd", ".ps1", ".msi", ".scr", ".js", ".vbs", ".com"
        };

        public ModerationService(GuapMessengerContext context) => _context = context;

        public async Task<MessageReportDto> ReportMessageAsync(Guid reporterId, ReportMessageRequest request, CancellationToken ct = default)
        {
            var msg = await _context.Messages.AsNoTracking()
                .FirstOrDefaultAsync(m => m.MessageId == request.MessageId, ct)
                ?? throw new InvalidOperationException("Сообщение не найдено");

            var report = new MessageReport
            {
                ReportId = Guid.NewGuid(),
                MessageId = msg.MessageId,
                ChatId = msg.ChatId,
                ReporterId = reporterId,
                Reason = request.Reason.Trim(),
                Comment = request.Comment?.Trim(),
                Status = "pending",
                CreatedAt = UtcDateTime.Now
            };
            _context.MessageReports.Add(report);
            await LogAsync("report", reporterId, msg.SenderId, msg.ChatId, msg.MessageId, request.Reason, ct);
            await _context.SaveChangesAsync(ct);

            return new MessageReportDto
            {
                ReportId = report.ReportId,
                MessageId = report.MessageId,
                ChatId = report.ChatId,
                ReporterId = report.ReporterId,
                Reason = report.Reason,
                Comment = report.Comment,
                Status = report.Status,
                CreatedAt = report.CreatedAt,
                MessagePreview = msg.MessageText?.Length > 120 ? msg.MessageText[..120] : msg.MessageText
            };
        }

        public async Task<UserMuteDto> MuteUserAsync(Guid actorId, MuteUserRequest request, CancellationToken ct = default)
        {
            if (request.DurationMinutes <= 0 || request.DurationMinutes > 60 * 24 * 30)
                throw new InvalidOperationException("Длительность mute: 1 мин — 30 дней");

            var mute = new UserMute
            {
                MuteId = Guid.NewGuid(),
                UserId = request.UserId,
                ChatId = request.ChatId,
                MutedBy = actorId,
                Reason = request.Reason,
                MutedUntil = UtcDateTime.Now.AddMinutes(request.DurationMinutes),
                CreatedAt = UtcDateTime.Now,
                IsActive = true
            };
            _context.UserMutes.Add(mute);
            await LogAsync("mute", actorId, request.UserId, request.ChatId, null,
                $"until={mute.MutedUntil:o}; {request.Reason}", ct);
            await _context.SaveChangesAsync(ct);

            return new UserMuteDto
            {
                MuteId = mute.MuteId,
                UserId = mute.UserId,
                ChatId = mute.ChatId,
                MutedUntil = mute.MutedUntil,
                Reason = mute.Reason,
                IsActive = true
            };
        }

        public async Task UnmuteAsync(Guid actorId, Guid muteId, CancellationToken ct = default)
        {
            var mute = await _context.UserMutes.FirstOrDefaultAsync(m => m.MuteId == muteId, ct);
            if (mute == null) return;
            mute.IsActive = false;
            await LogAsync("unmute", actorId, mute.UserId, mute.ChatId, null, null, ct);
            await _context.SaveChangesAsync(ct);
        }

        public async Task<bool> IsUserMutedAsync(Guid userId, Guid? chatId, CancellationToken ct = default)
        {
            var now = UtcDateTime.Now;
            return await _context.UserMutes.AsNoTracking().AnyAsync(m =>
                m.UserId == userId && m.IsActive && m.MutedUntil > now &&
                (m.ChatId == null || m.ChatId == chatId), ct);
        }

        public Task<(bool blocked, string? reason)> CheckContentAsync(string? text, IEnumerable<string>? fileNames, CancellationToken ct = default)
        {
            if (!string.IsNullOrEmpty(text))
            {
                var matches = SuspiciousUrl.Matches(text);
                foreach (Match m in matches)
                {
                    var url = m.Value.ToLowerInvariant();
                    if (url.Contains("bit.ly") || url.Contains("tinyurl") || url.Contains(".tk/") || url.Contains("free-prize"))
                        return Task.FromResult<(bool, string?)>((true, "Подозрительная ссылка заблокирована автомодерацией"));
                }
            }

            if (fileNames != null)
            {
                foreach (var name in fileNames)
                {
                    var ext = Path.GetExtension(name ?? "");
                    if (BlockedExtensions.Contains(ext))
                        return Task.FromResult<(bool, string?)>((true, $"Тип файла {ext} запрещён"));
                }
            }

            return Task.FromResult<(bool, string?)>((false, null));
        }

        public async Task LogAsync(string action, Guid? actorId, Guid? targetUserId, Guid? chatId, Guid? messageId, string? details, CancellationToken ct = default)
        {
            _context.ModerationLogs.Add(new ModerationLog
            {
                LogId = Guid.NewGuid(),
                ActorId = actorId,
                TargetUserId = targetUserId,
                ChatId = chatId,
                MessageId = messageId,
                Action = action,
                Details = details,
                CreatedAt = UtcDateTime.Now
            });
            await _context.SaveChangesAsync(ct);
        }

        public async Task<List<MessageReportDto>> GetPendingReportsAsync(int take = 50, CancellationToken ct = default)
        {
            return await _context.MessageReports.AsNoTracking()
                .Where(r => r.Status == "pending")
                .OrderByDescending(r => r.CreatedAt)
                .Take(take)
                .Select(r => new MessageReportDto
                {
                    ReportId = r.ReportId,
                    MessageId = r.MessageId,
                    ChatId = r.ChatId,
                    ReporterId = r.ReporterId,
                    Reason = r.Reason,
                    Comment = r.Comment,
                    Status = r.Status,
                    CreatedAt = r.CreatedAt
                })
                .ToListAsync(ct);
        }

        public async Task ReviewReportAsync(Guid reportId, Guid adminId, string status, string? note, CancellationToken ct = default)
        {
            var report = await _context.MessageReports.FirstOrDefaultAsync(r => r.ReportId == reportId, ct);
            if (report == null) return;
            report.Status = status;
            report.ReviewedAt = UtcDateTime.Now;
            report.ReviewedBy = adminId;
            report.AdminNote = note;
            await LogAsync(status == "dismissed" ? "dismiss_report" : "review_report", adminId, null, report.ChatId, report.MessageId, note, ct);
            await _context.SaveChangesAsync(ct);
        }

        public async Task<List<ModerationLogDto>> GetLogsAsync(int take = 100, CancellationToken ct = default)
        {
            return await _context.ModerationLogs.AsNoTracking()
                .OrderByDescending(l => l.CreatedAt)
                .Take(take)
                .Select(l => new ModerationLogDto
                {
                    LogId = l.LogId,
                    ActorId = l.ActorId,
                    TargetUserId = l.TargetUserId,
                    ChatId = l.ChatId,
                    MessageId = l.MessageId,
                    Action = l.Action,
                    Details = l.Details,
                    CreatedAt = l.CreatedAt
                })
                .ToListAsync(ct);
        }

        public async Task<List<UserMuteDto>> GetActiveMutesAsync(CancellationToken ct = default)
        {
            var now = UtcDateTime.Now;
            return await _context.UserMutes.AsNoTracking()
                .Where(m => m.IsActive && m.MutedUntil > now)
                .OrderByDescending(m => m.MutedUntil)
                .Select(m => new UserMuteDto
                {
                    MuteId = m.MuteId,
                    UserId = m.UserId,
                    ChatId = m.ChatId,
                    MutedUntil = m.MutedUntil,
                    Reason = m.Reason,
                    IsActive = m.IsActive
                })
                .ToListAsync(ct);
        }
        public async Task DeleteLogAsync(Guid logId, CancellationToken ct = default)
        {
            var log = await _context.ModerationLogs.FirstOrDefaultAsync(l => l.LogId == logId, ct);
            if (log == null) return;
            _context.ModerationLogs.Remove(log);
            await _context.SaveChangesAsync(ct);
        }
        public async Task DeleteReportAsync(Guid reportId, Guid adminId, CancellationToken ct = default)
        {
            var report = await _context.MessageReports.FirstOrDefaultAsync(r => r.ReportId == reportId, ct);
            if (report == null) return;
            await LogAsync("delete_report", adminId, report.ReporterId, report.ChatId, report.MessageId, "report removed", ct);
            _context.MessageReports.Remove(report);
            await _context.SaveChangesAsync(ct);
        }
    }
}
