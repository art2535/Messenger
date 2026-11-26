using Messenger.Core.DTOs.Messages;
using Microsoft.AspNetCore.Http;

namespace Messenger.Infrastructure.Services
{
    public interface IMessageSendingService
    {
        Task<MessageDto> SendAsync(
            Guid chatId,
            Guid senderId,
            string? messageText,
            IFormFile[]? files = null,
            CancellationToken cancellationToken = default);
    }
}
