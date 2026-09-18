using Asp.Versioning;
using Messenger.API.Responses;
using Messenger.Core.DTOs.Auth;
using Messenger.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для авторизации пользователей через ЕТА ГУАП
    /// </summary>
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [Tags("Authorization")]
    public class AuthorizationController : ControllerBase
    {
        private readonly IUserService _userService;

        public AuthorizationController(IUserService userService)
        {
            _userService = userService;
        }

        /// <summary>
        /// Аутентификация пользователя через ЕТА
        /// </summary>
        [HttpPost("external/callback")]
        [EndpointName("ExternalCallback")]
        [EndpointSummary("Аутентификация пользователя через ЕТА")]
        [EndpointDescription("Выполняет запись входа в базу данных. Если пользователь с указанным externalId не найден — создаёт нового.")]
        [ProducesResponseType(typeof(LoginEtaResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ExternalCallbackAsync(
            [FromBody, Description("Данные пользователя из ЕТА ГУАП")] ExternalLoginRequest request)
        {
            try
            {
                var user = await _userService.GetUserByExternalIdAsync(request.ExternalId);

                user ??= await _userService.RegisterExternalUserAsync(request.ExternalId, request.Email, request.FirstName,
                        request.LastName, request.Roles);

                var fullName = string.Join(" ", new[] { user.FirstName, user.LastName }
                    .Where(s => !string.IsNullOrEmpty(s)));

                return Ok(new LoginEtaResponse
                {
                    IsSuccess = true,
                    UserId = user!.UserId,
                    FullName = fullName
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }
    }
}
