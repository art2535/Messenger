using Microsoft.AspNetCore.Http;

namespace Messenger.Infrastructure.Interfaces.Authentication
{
    public sealed record OAuthProxyResult(int StatusCode, string ResponseBody);

    public interface IOAuthProxyWorkflowService
    {
        Task<OAuthProxyResult> RequestTokenAsync(
            IFormCollection form,
            CancellationToken cancellationToken = default);
    }
}
