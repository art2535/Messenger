using Asp.Versioning;
using Messenger.Core.Interfaces;
using Messenger.Infrastructure.Interfaces.Authentication;
using Messenger.Core.Responses;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для авторизации пользователей через ЕТА ГУАП.
    /// </summary>
    [ApiController]
    [Route("api/v{version:apiVersion}/auth")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [Tags("Authorization")]
    public class AuthorizationController : ControllerBase
    {
        private readonly IAuthorizationWorkflowService _authorizationWorkflowService;
        private readonly ILogger<AuthorizationController> _logger;

        public AuthorizationController(
            IAuthorizationWorkflowService authorizationWorkflowService,
            ILogger<AuthorizationController> logger)
        {
            _authorizationWorkflowService = authorizationWorkflowService;
            _logger = logger;
        }

        /// <summary>
        /// Аутентификация пользователя через ЕТА.
        /// </summary>
        [HttpPost("external/callback")]
        [EndpointName("ExternalCallback")]
        [EndpointSummary("Аутентификация пользователя через ЕТА")]
        [EndpointDescription("Выполняет запись входа в базу данных. Если пользователь с указанным externalId не найден — создаёт нового.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostExternalCallbackAsync(
            [FromBody, Description("Данные пользователя из ЕТА ГУАП")] ExternalLoginRequest request,
            CancellationToken ct = default)
        {
            try
            {
                var user = await _authorizationWorkflowService.GetOrRegisterExternalUserAsync(
                    request.ExternalId,
                    request.Email,
                    request.FirstName,
                    request.LastName,
                    request.Roles);

                var fullName = string.Join(" ", new[] { user.FirstName, user.LastName }
                    .Where(s => !string.IsNullOrEmpty(s)));

                return Ok(ApiResponse.Success(new { userId = user!.UserId, fullName }, "Авторизация успешна"));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении POST /auth/external/callback");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }
    }
}