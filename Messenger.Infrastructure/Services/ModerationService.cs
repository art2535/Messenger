using Messenger.Application.Common;
using System.Text.RegularExpressions;
using Messenger.Application.DTOs.Moderation;
using Messenger.Application.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Services
{
    public class ModerationService : IModerationService
    {
        private readonly GuapMessengerContext _context;
        private readonly IAdminService _adminService;

        private static readonly Regex SuspiciousUrl = new(
            @"https?://[^\s]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".bat", ".cmd", ".ps1", ".msi", ".scr", ".js", ".vbs", ".com"
        };

        public ModerationService(GuapMessengerContext context, IAdminService adminService)
        {
            _context = context;
            _adminService = adminService;
        }

        private async Task EnsureAdminAsync(Guid actorId, CancellationToken ct)
        {
            if (actorId == Guid.Empty || !await _adminService.IsSystemAdminAsync(actorId, ct))
                throw new UnauthorizedAccessException("Нужна роль «Администратор»");
        }

        public async Task<MessageReportDto> ReportMessageAsync(Guid reporterId, ReportMessageRequest request, CancellationToken ct = default)
        {
            var msg = await _context.Messages.AsNoTracking()
                .FirstOrDefaultAsync(m => m.MessageId == request.MessageId, ct)
                ?? throw new InvalidOperationException("Сообщение не найдено");

            var isParticipant = await _context.ChatParticipants.AsNoTracking()
                .AnyAsync(p => p.ChatId == msg.ChatId && p.UserId == reporterId, ct);
            if (!isParticipant)
                throw new UnauthorizedAccessException("Пожаловаться можно только на сообщение из чата, участником которого вы являетесь");

            var reason = request.Reason?.Trim();
            var comment = request.Comment?.Trim();
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("Причина жалобы обязательна");
            if (reason.Length > 64)
                throw new ArgumentException("Причина жалобы не должна превышать 64 символа");
            if (comment?.Length > 1000)
                throw new ArgumentException("Комментарий не должен превышать 1000 символов");

            var report = new MessageReport
            {
                ReportId = Guid.NewGuid(),
                MessageId = msg.MessageId,
                ChatId = msg.ChatId,
                ReporterId = reporterId,
                Reason = reason,
                Comment = comment,
                Status = "pending",
                CreatedAt = UtcDateTime.Now
            };
            _context.MessageReports.Add(report);
            AddLog("report", reporterId, msg.SenderId, msg.ChatId, msg.MessageId, request.Reason);
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
            await EnsureAdminAsync(actorId, ct);
            if (request.DurationMinutes <= 0 || request.DurationMinutes > 60 * 24 * 30)
                throw new InvalidOperationException("Длительность mute: 1 мин — 30 дней");
            if (request.Reason?.Length > 500)
                throw new ArgumentException("Причина mute не должна превышать 500 символов");

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
            AddLog("mute", actorId, request.UserId, request.ChatId, null,
                $"until={mute.MutedUntil:o}; {request.Reason}");
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
            await EnsureAdminAsync(actorId, ct);
            var mute = await _context.UserMutes.FirstOrDefaultAsync(m => m.MuteId == muteId, ct);
            if (mute == null) return;
            mute.IsActive = false;
            AddLog("unmute", actorId, mute.UserId, mute.ChatId, null, null);
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
            AddLog(action, actorId, targetUserId, chatId, messageId, details);
            await _context.SaveChangesAsync(ct);
        }

        private void AddLog(string action, Guid? actorId, Guid? targetUserId, Guid? chatId, Guid? messageId, string? details)
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
        }

        public async Task<List<MessageReportDto>> GetPendingReportsAsync(Guid adminId, int take = 50, CancellationToken ct = default)
        {
            await EnsureAdminAsync(adminId, ct);
            take = Math.Clamp(take, 1, 200);
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
            await EnsureAdminAsync(adminId, ct);
            var normalizedStatus = status?.Trim().ToLowerInvariant();
            if (normalizedStatus is not ("resolved" or "dismissed"))
                throw new ArgumentException("Статус жалобы должен быть resolved или dismissed");
            if (note?.Length > 1000)
                throw new ArgumentException("Примечание администратора не должно превышать 1000 символов");

            // Conditional UPDATE makes review single-winner under concurrent admin requests.
            // The status transition and its audit record commit in the same transaction.
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);

            var reviewedAt = UtcDateTime.Now;
            var adminNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            var updated = await _context.MessageReports
                .Where(r => r.ReportId == reportId && r.Status == "pending")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.Status, normalizedStatus)
                    .SetProperty(r => r.ReviewedAt, reviewedAt)
                    .SetProperty(r => r.ReviewedBy, adminId)
                    .SetProperty(r => r.AdminNote, adminNote), ct);

            if (updated == 0)
                return;

            var report = await _context.MessageReports
                .AsNoTracking()
                .Where(r => r.ReportId == reportId)
                .Select(r => new { r.ChatId, r.MessageId })
                .FirstAsync(ct);

            AddLog(
                normalizedStatus == "dismissed" ? "dismiss_report" : "review_report",
                adminId,
                null,
                report.ChatId,
                report.MessageId,
                adminNote);

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        public async Task<List<ModerationLogDto>> GetLogsAsync(Guid adminId, int take = 100, CancellationToken ct = default)
        {
            await EnsureAdminAsync(adminId, ct);
            take = Math.Clamp(take, 1, 500);
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

        public async Task<List<UserMuteDto>> GetActiveMutesAsync(Guid adminId, CancellationToken ct = default)
        {
            await EnsureAdminAsync(adminId, ct);
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
        public async Task DeleteLogAsync(Guid adminId, Guid logId, CancellationToken ct = default)
        {
            await EnsureAdminAsync(adminId, ct);
            var log = await _context.ModerationLogs.FirstOrDefaultAsync(l => l.LogId == logId, ct);
            if (log == null) return;
            _context.ModerationLogs.Remove(log);
            await _context.SaveChangesAsync(ct);
        }
        public async Task DeleteReportAsync(Guid reportId, Guid adminId, CancellationToken ct = default)
        {
            await EnsureAdminAsync(adminId, ct);
            var report = await _context.MessageReports.FirstOrDefaultAsync(r => r.ReportId == reportId, ct);
            if (report == null) return;
            AddLog("delete_report", adminId, report.ReporterId, report.ChatId, report.MessageId, "report removed");
            _context.MessageReports.Remove(report);
            await _context.SaveChangesAsync(ct);
        }
    }
}
