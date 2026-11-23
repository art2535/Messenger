using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер прокси OAuth 2.0 token endpoint для Scalar UI.
    /// Обходит CORS при обмене authorization_code/refresh_token с SSO ГУАП.
    /// </summary>
    [ApiController]
    [Route("oauth")]
    [AllowAnonymous]
    [ApiVersionNeutral]
    [Produces("application/json")]
    [Tags("OAuth")]
    public class OAuthProxyController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<OAuthProxyController> _logger;

        public OAuthProxyController(
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<OAuthProxyController> logger)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <summary>
        /// Проксирует запрос на получение токена в Keycloak SSO ГУАП.
        /// </summary>
        [HttpPost("token")]
        [EndpointName("OAuthProxyToken")]
        [EndpointSummary("Прокси-запрос OAuth token")]
        [EndpointDescription("Проксирует token request на Keycloak SSO ГУАП. " +
            "Нужен для Scalar UI: обход CORS и поддержка authorization_code + refresh_token.")]
        [Consumes("application/x-www-form-urlencoded")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostTokenAsync(CancellationToken ct = default)
        {
            try
            {
                var authority = $"{_configuration["AzureAd:Instance"]?.TrimEnd('/')}/{_configuration["AzureAd:TenantId"]}";
                var tokenUrl = $"{authority}/protocol/openid-connect/token";

                var form = await Request.ReadFormAsync(ct);
                var pairs = form
                    .SelectMany(kv => kv.Value.Select(v => new KeyValuePair<string, string>(kv.Key, v ?? string.Empty)))
                    .ToList();

                using var content = new FormUrlEncodedContent(pairs);
                var client = _httpClientFactory.CreateClient();

                using var response = await client.PostAsync(tokenUrl, content, ct);
                var responseBody = await response.Content.ReadAsStringAsync(ct);

                if (string.IsNullOrWhiteSpace(responseBody))
                {
                    return StatusCode((int)response.StatusCode, new
                    {
                        IsSuccess = false,
                        Error = "Keycloak возвратил пустое тело"
                    });
                }

                // Примечание: для успешного проксирования возвращается оригинальный ответ Keycloak 
                // через ContentResult, чтобы не сломать OAuth-поток на клиенте (Scalar UI ожидает 
                // сырой JSON с токеном, а не обёрнутый анонимный объект).
                return new ContentResult
                {
                    StatusCode = (int)response.StatusCode,
                    Content = responseBody,
                    ContentType = "application/json; charset=utf-8"
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Ошибка сети при проксировании запроса к Keycloak SSO ГУАП");
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении POST /oauth/token");
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }
    }
}