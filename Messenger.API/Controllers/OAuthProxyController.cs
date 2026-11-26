using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Messenger.Core.Responses;
using Messenger.Infrastructure.Interfaces.Authentication;

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
        private readonly IOAuthProxyWorkflowService _oauthProxyWorkflowService;
        private readonly ILogger<OAuthProxyController> _logger;

        public OAuthProxyController(
            IOAuthProxyWorkflowService oauthProxyWorkflowService,
            ILogger<OAuthProxyController> logger)
        {
            _oauthProxyWorkflowService = oauthProxyWorkflowService;
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
                var form = await Request.ReadFormAsync(ct);
                var result = await _oauthProxyWorkflowService.RequestTokenAsync(form, ct);

                if (string.IsNullOrWhiteSpace(result.ResponseBody))
                {
                    return StatusCode(
                        result.StatusCode,
                        ApiResponse.Failure("Keycloak возвратил пустое тело", "INTERNAL_ERROR"));
                }

                return new ContentResult
                {
                    StatusCode = result.StatusCode,
                    Content = result.ResponseBody,
                    ContentType = "application/json; charset=utf-8"
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Ошибка сети при проксировании запроса к Keycloak SSO ГУАП");
                return StatusCode(StatusCodes.Status502BadGateway, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении POST /oauth/token");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }
    }
}