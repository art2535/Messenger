using Messenger.Core.Models;

namespace Messenger.Infrastructure.Services
{
    public interface IMessageCreationService
    {
        Task CreateMessageAsync(Message message, CancellationToken token = default);
    }
}
