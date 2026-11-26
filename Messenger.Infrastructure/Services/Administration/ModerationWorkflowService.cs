using Messenger.Core.DTOs.Moderation;
using Messenger.Core.Interfaces;
using Messenger.Infrastructure.Interfaces.Administration;

namespace Messenger.Infrastructure.Services.Administration
{
    public sealed class ModerationWorkflowService : IModerationWorkflowService
    {
        private readonly IModerationService _moderationService;

        public ModerationWorkflowService(IModerationService moderationService)
        {
            _moderationService = moderationService;
        }

        public Task<MessageReportDto> ReportMessageAsync(
            Guid reporterId,
            ReportMessageRequest request,
            CancellationToken cancellationToken = default)
        {
            return _moderationService.ReportMessageAsync(
                reporterId,
                request,
                cancellationToken);
        }

        public Task<UserMuteDto> MuteUserAsync(
            Guid adminUserId,
            MuteUserRequest request,
            CancellationToken cancellationToken = default)
        {
            return _moderationService.MuteUserAsync(
                adminUserId,
                request,
                cancellationToken);
        }

        public Task UnmuteAsync(
            Guid adminUserId,
            Guid muteId,
            CancellationToken cancellationToken = default)
        {
            return _moderationService.UnmuteAsync(
                adminUserId,
                muteId,
                cancellationToken);
        }
    }
}
