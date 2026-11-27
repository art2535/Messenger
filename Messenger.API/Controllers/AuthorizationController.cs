using Messenger.Application.DTOs.Auth;
using Messenger.Application.DTOs.Responses;
﻿using Asp.Versioning;
using Messenger.Application.Interfaces;
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
        private readonly IUserService _userService;
        private readonly ILogger<AuthorizationController> _logger;

        public AuthorizationController(
            IUserService userService,
            ILogger<AuthorizationController> logger)
        {
            _userService = userService;
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
                var user = await _userService.GetUserByExternalIdAsync(request.ExternalId, ct);

                user ??= await _userService.RegisterExternalUserAsync(
                    request.ExternalId,
                    request.Email,
                    request.FirstName,
                    request.LastName,
                    request.Roles,
                    ct);

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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении POST /auth/external/callback");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }
    }
}