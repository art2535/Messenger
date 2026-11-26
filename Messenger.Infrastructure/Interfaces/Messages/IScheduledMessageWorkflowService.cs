using Messenger.Core.DTOs.Messages;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Messenger.Infrastructure.Interfaces.Messages
{
    public interface IScheduledMessageWorkflowService
    {
        Task<ScheduledMessageDto> ScheduleAsync(
            User user,
            Guid chatId,
            string? messageText,
            DateTimeOffset? scheduledAt,
            string? replyToMessageId,
            IFormFile[]? files,
            CancellationToken cancellationToken = default);

        Task<List<ScheduledMessageDto>> GetPendingByUserAsync(
            User user,
            CancellationToken cancellationToken = default);

        Task<List<ScheduledMessageDto>> GetPendingByChatAsync(
            User user,
            Guid chatId,
            CancellationToken cancellationToken = default);

        Task<ScheduledMessageDto?> UpdateAsync(
            User user,
            Guid scheduledMessageId,
            UpdateScheduledMessageRequest request,
            CancellationToken cancellationToken = default);

        Task<bool> CancelAsync(
            User user,
            Guid scheduledMessageId,
            CancellationToken cancellationToken = default);
    }

    public sealed class ScheduledMessageForbiddenException : Exception
    {
        public ScheduledMessageForbiddenException(string message) : base(message)
        {
        }
    }

    public sealed class ScheduledMessageChatNotFoundException : Exception
    {
        public ScheduledMessageChatNotFoundException(string message) : base(message)
        {
        }
    }
}
