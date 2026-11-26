using Messenger.Core.DTOs.Moderation;

namespace Messenger.Infrastructure.Interfaces.Administration
{
    public sealed class AdminForbiddenAccessException : Exception
    {
        public AdminForbiddenAccessException(string message) : base(message)
        {
        }
    }

    public interface IAdminWorkflowService
    {
        Task<object> GetAdminInfoAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<object> GetActivityStatsAsync(CancellationToken cancellationToken = default);
        Task EnsureAdminAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<List<MessageReportDto>?> GetPendingReportsAsync(
            int take,
            CancellationToken cancellationToken = default);
        Task ReviewReportAsync(
            Guid reportId,
            Guid adminUserId,
            string status,
            string? note,
            CancellationToken cancellationToken = default);
        Task<List<UserMuteDto>?> GetActiveMutesAsync(CancellationToken cancellationToken = default);
        Task<List<ModerationLogDto>?> GetLogsAsync(
            int take,
            CancellationToken cancellationToken = default);
        Task DeleteReportAsync(
            Guid reportId,
            Guid adminUserId,
            CancellationToken cancellationToken = default);
        Task DeleteLogAsync(Guid logId, CancellationToken cancellationToken = default);
    }
}
