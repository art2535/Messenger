using Messenger.Application.DTOs.Responses;
﻿using Asp.Versioning;
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
                const int maxFormBytes = 16 * 1024;
                if (Request.ContentLength is > maxFormBytes)
                    return StatusCode(StatusCodes.Status413PayloadTooLarge,
                        ApiResponse.Failure("Слишком большой запрос", "PAYLOAD_TOO_LARGE"));

                var authority = $"{_configuration["AzureAd:Instance"]?.TrimEnd('/')}/{_configuration["AzureAd:TenantId"]}";
                var tokenUrl = $"{authority}/protocol/openid-connect/token";

                var form = await Request.ReadFormAsync(ct);
                var allowedKeys = new HashSet<string>(StringComparer.Ordinal)
                {
                    "grant_type", "client_id", "client_secret", "code", "redirect_uri",
                    "refresh_token", "scope", "code_verifier"
                };

                if (form.Keys.Any(key => !allowedKeys.Contains(key)))
                    return BadRequest(ApiResponse.Failure("Запрос содержит недопустимые параметры", "INVALID_REQUEST"));

                var grantType = form["grant_type"].ToString();
                if (!string.Equals(grantType, "authorization_code", StringComparison.Ordinal) &&
                    !string.Equals(grantType, "refresh_token", StringComparison.Ordinal))
                {
                    return BadRequest(ApiResponse.Failure("Неподдерживаемый grant_type", "INVALID_REQUEST"));
                }

                var hasCode = !string.IsNullOrWhiteSpace(form["code"].ToString());
                var hasRefreshToken = !string.IsNullOrWhiteSpace(form["refresh_token"].ToString());
                if (string.Equals(grantType, "authorization_code", StringComparison.Ordinal) && !hasCode)
                    return BadRequest(ApiResponse.Failure("Отсутствует authorization code", "INVALID_REQUEST"));
                if (string.Equals(grantType, "refresh_token", StringComparison.Ordinal) && !hasRefreshToken)
                    return BadRequest(ApiResponse.Failure("Отсутствует refresh token", "INVALID_REQUEST"));

                var pairs = form
                    .Where(kv => allowedKeys.Contains(kv.Key))
                    .SelectMany(kv => kv.Value.Select(v => new KeyValuePair<string, string>(kv.Key, v ?? string.Empty)))
                    .ToList();

                using var content = new FormUrlEncodedContent(pairs);
                var client = _httpClientFactory.CreateClient();

                using var response = await client.PostAsync(tokenUrl, content, ct);
                var responseBody = await response.Content.ReadAsStringAsync(ct);

                if (string.IsNullOrWhiteSpace(responseBody))
                {
                    return StatusCode((int)response.StatusCode, ApiResponse.Failure("Провайдер авторизации вернул пустой ответ", "UPSTREAM_ERROR"));
                }

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
                return StatusCode(StatusCodes.Status502BadGateway, ApiResponse.Failure("Провайдер авторизации недоступен", "UPSTREAM_ERROR"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении POST /oauth/token");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure("Внутренняя ошибка OAuth proxy", "INTERNAL_ERROR"));
            }
        }
    }
}