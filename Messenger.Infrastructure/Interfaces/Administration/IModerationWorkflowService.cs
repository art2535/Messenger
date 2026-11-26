using Messenger.Core.DTOs.Moderation;

namespace Messenger.Infrastructure.Interfaces.Administration
{
    public interface IModerationWorkflowService
    {
        Task<MessageReportDto> ReportMessageAsync(
            Guid reporterId,
            ReportMessageRequest request,
            CancellationToken cancellationToken = default);
        Task<UserMuteDto> MuteUserAsync(
            Guid adminUserId,
            MuteUserRequest request,
            CancellationToken cancellationToken = default);
        Task UnmuteAsync(
            Guid adminUserId,
            Guid muteId,
            CancellationToken cancellationToken = default);
    }
}
