using Messenger.Infrastructure.Interfaces.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Messenger.Infrastructure.Services.Authentication
{
    public sealed class OAuthProxyWorkflowService : IOAuthProxyWorkflowService
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        public OAuthProxyWorkflowService(
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<OAuthProxyResult> RequestTokenAsync(
            IFormCollection form,
            CancellationToken cancellationToken = default)
        {
            var authority =
                $"{_configuration["AzureAd:Instance"]?.TrimEnd('/')}/{_configuration["AzureAd:TenantId"]}";
            var tokenUrl = $"{authority}/protocol/openid-connect/token";

            var pairs = form
                .SelectMany(item => item.Value.Select(value =>
                    new KeyValuePair<string, string>(item.Key, value ?? string.Empty)))
                .ToList();

            using var content = new FormUrlEncodedContent(pairs);
            var client = _httpClientFactory.CreateClient();

            using var response = await client.PostAsync(tokenUrl, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            return new OAuthProxyResult((int)response.StatusCode, responseBody);
        }
    }
}
