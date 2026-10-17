using System.Net.Http.Headers;
using System.Text.Json;

namespace Messenger.Web.Helpers
{
    public class ApiHelper
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public ApiHelper(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        public string GetApiUrl(string? path = null)
        {
            var baseUrl = _configuration["URL:API:HTTPS"]?.TrimEnd('/');
            var version = _configuration["URL:API:Version"] ?? "1.0";

            string url = $"{baseUrl}/api/v{version}";

            if (!string.IsNullOrWhiteSpace(path))
            {
                url += "/" + path.TrimStart('/');
            }

            return url;
        }

        private HttpClient CreateClient(string? accessToken = null)
        {
            var client = _httpClientFactory.CreateClient("Api");

            if (!string.IsNullOrEmpty(accessToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            return client;
        }

        public async Task<HttpResponseMessage> GetRawAsync(string path, string? accessToken = null, CancellationToken ct = default)
        {
            var client = CreateClient(accessToken);
            return await client.GetAsync(GetApiUrl(path), ct);
        }

        public async Task<T?> GetAsync<T>(string path, string? accessToken = null, CancellationToken ct = default)
        {
            var response = await GetRawAsync(path, accessToken, ct);
            response.EnsureSuccessStatusCode();

            if (response.StatusCode == System.Net.HttpStatusCode.NoContent
                || response.Content.Headers.ContentLength == 0)
            {
                return default;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is not null
                && !contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                var preview = await response.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException(
                    $"Ожидался JSON, получен Content-Type={contentType}. Body: {Truncate(preview, 300)}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(json))
                return default;

            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";

        public async Task<HttpResponseMessage> PostRawAsync<TRequest>(string path, TRequest body, string? accessToken = null,
            CancellationToken ct = default)
        {
            var client = CreateClient(accessToken);
            return await client.PostAsJsonAsync(GetApiUrl(path), body, JsonOptions, ct);
        }

        public async Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest body, string? accessToken = null,
            CancellationToken ct = default)
        {
            var response = await PostRawAsync(path, body, accessToken, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, ct);
        }

        public async Task<HttpResponseMessage> PutRawAsync<TRequest>(string path, TRequest body, string? accessToken = null,
            CancellationToken ct = default)
        {
            var client = CreateClient(accessToken);
            return await client.PutAsJsonAsync(GetApiUrl(path), body, JsonOptions, ct);
        }

        public async Task<TResponse?> PutAsync<TRequest, TResponse>(string path, TRequest body, string? accessToken = null,
            CancellationToken ct = default)
        {
            var response = await PutRawAsync(path, body, accessToken, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, ct);
        }

        public async Task<HttpResponseMessage> PatchRawAsync(string path, string? accessToken = null, CancellationToken ct = default)
        {
            var client = CreateClient(accessToken);
            var request = new HttpRequestMessage(HttpMethod.Patch, GetApiUrl(path));
            if (!string.IsNullOrEmpty(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
            return await client.SendAsync(request, ct);
        }

        public async Task<HttpResponseMessage> PatchRawAsync<TRequest>(string path, TRequest body, string? accessToken = null,
            CancellationToken ct = default)
        {
            var client = CreateClient(accessToken);
            return await client.PatchAsJsonAsync(GetApiUrl(path), body, JsonOptions, ct);
        }

        public async Task<HttpResponseMessage> DeleteRawAsync(string path, string? accessToken = null,
            CancellationToken ct = default)
        {
            var client = CreateClient(accessToken);
            return await client.DeleteAsync(GetApiUrl(path), ct);
        }

        public async Task<HttpResponseMessage> PostMultipartRawAsync(string path, MultipartFormDataContent content,
            string? accessToken = null, CancellationToken ct = default)
        {
            var client = CreateClient(accessToken);
            return await client.PostAsync(GetApiUrl(path), content, ct);
        }
    }
}