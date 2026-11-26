using Messenger.Core.Models;

namespace Messenger.Infrastructure.Interfaces.Messages
{
    public interface IMessageCreationService
    {
        Task CreateMessageAsync(Message message, CancellationToken token = default);
    }
}
