using Messenger.Core.DTOs.Moderation;

namespace Messenger.Core.Interfaces
{
    public interface IModerationService
    {
        Task<MessageReportDto> ReportMessageAsync(Guid reporterId, ReportMessageRequest request, CancellationToken ct = default);
        Task<UserMuteDto> MuteUserAsync(Guid actorId, MuteUserRequest request, CancellationToken ct = default);
        Task UnmuteAsync(Guid actorId, Guid muteId, CancellationToken ct = default);
        Task<bool> IsUserMutedAsync(Guid userId, Guid? chatId, CancellationToken ct = default);
        Task<(bool blocked, string? reason)> CheckContentAsync(string? text, IEnumerable<string>? fileNames, CancellationToken ct = default);
        Task LogAsync(string action, Guid? actorId, Guid? targetUserId, Guid? chatId, Guid? messageId, string? details, CancellationToken ct = default);
        Task<List<MessageReportDto>> GetPendingReportsAsync(int take = 50, CancellationToken ct = default);
        Task ReviewReportAsync(Guid reportId, Guid adminId, string status, string? note, CancellationToken ct = default);
        Task<List<ModerationLogDto>> GetLogsAsync(int take = 100, CancellationToken ct = default);
        Task<List<UserMuteDto>> GetActiveMutesAsync(CancellationToken ct = default);
        Task DeleteLogAsync(Guid logId, CancellationToken ct = default);
        Task DeleteReportAsync(Guid reportId, Guid adminId, CancellationToken ct = default);
    }
}
