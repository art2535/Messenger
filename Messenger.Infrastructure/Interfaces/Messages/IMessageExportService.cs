using Messenger.Core.DTOs;
using Messenger.Core.Models;

namespace Messenger.Infrastructure.Interfaces.Messages
{
    public interface IMessageExportService
    {
        ChatExportResult Export(
            Guid chatId,
            string format,
            string? chatName,
            IReadOnlyCollection<Message> messages);
    }
}