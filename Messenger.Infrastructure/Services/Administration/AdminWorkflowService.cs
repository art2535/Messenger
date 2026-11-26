using Messenger.Core.DTOs.Moderation;
using Messenger.Core.Interfaces;
using Messenger.Infrastructure.Interfaces.Administration;

namespace Messenger.Infrastructure.Services.Administration
{
    public sealed class AdminWorkflowService : IAdminWorkflowService
    {
        private readonly IAdminService _adminService;
        private readonly IUserService _userService;
        private readonly IModerationService _moderationService;

        public AdminWorkflowService(
            IAdminService adminService,
            IUserService userService,
            IModerationService moderationService)
        {
            _adminService = adminService;
            _userService = userService;
            _moderationService = moderationService;
        }

        public async Task<object> GetAdminInfoAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var isAdmin = await _adminService.IsSystemAdminAsync(userId, cancellationToken);
            var user = await _userService.GetUserByIdAsync(userId, cancellationToken);

            return new
            {
                isAdmin,
                userId,
                login = user?.Login
            };
        }

        public async Task<object> GetActivityStatsAsync(
            CancellationToken cancellationToken = default)
        {
            return await _adminService.GetActivityStatsAsync(cancellationToken);
        }

        public async Task EnsureAdminAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var isAdmin = await _adminService.IsSystemAdminAsync(userId, cancellationToken);
            if (!isAdmin)
            {
                throw new AdminForbiddenAccessException("Нужна роль «Администратор»");
            }
        }

        public Task<List<MessageReportDto>?> GetPendingReportsAsync(
            int take,
            CancellationToken cancellationToken = default)
        {
            return _moderationService.GetPendingReportsAsync(take, cancellationToken);
        }

        public Task ReviewReportAsync(
            Guid reportId,
            Guid adminUserId,
            string status,
            string? note,
            CancellationToken cancellationToken = default)
        {
            return _moderationService.ReviewReportAsync(
                reportId,
                adminUserId,
                status,
                note,
                cancellationToken);
        }

        public Task<List<UserMuteDto>?> GetActiveMutesAsync(
            CancellationToken cancellationToken = default)
        {
            return _moderationService.GetActiveMutesAsync(cancellationToken);
        }

        public Task<List<ModerationLogDto>?> GetLogsAsync(
            int take,
            CancellationToken cancellationToken = default)
        {
            return _moderationService.GetLogsAsync(take, cancellationToken);
        }

        public Task DeleteReportAsync(
            Guid reportId,
            Guid adminUserId,
            CancellationToken cancellationToken = default)
        {
            return _moderationService.DeleteReportAsync(reportId, adminUserId, cancellationToken);
        }

        public Task DeleteLogAsync(
            Guid logId,
            CancellationToken cancellationToken = default)
        {
            return _moderationService.DeleteLogAsync(logId, cancellationToken);
        }
    }
}
