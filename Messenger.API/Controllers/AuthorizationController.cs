using Messenger.Application.DTOs.Auth;
using Messenger.Application.DTOs.Responses;
﻿using Asp.Versioning;
using Messenger.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер первичной регистрации пользователя по проверенному OIDC access token.
    /// </summary>
    [Authorize]
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
        /// Создать локальную запись пользователя по claims валидированного токена ЕТА.
        /// </summary>
        [HttpPost("external/callback")]
        [EndpointName("ExternalCallback")]
        [EndpointSummary("Регистрация пользователя через ЕТА")]
        [EndpointDescription("Создаёт локальную запись по идентификатору и ролям из проверенного OIDC access token.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostExternalCallbackAsync(
            [FromBody, Description("Совместимый с Web payload; идентификатор и роли проверяются по JWT claims")] ExternalLoginRequest? request,
            CancellationToken ct = default)
        {
            try
            {
                if (request == null)
                    return BadRequest(ApiResponse.Failure("Тело запроса отсутствует", "INVALID_REQUEST"));

                var externalId = User.FindFirst("sub")?.Value
                    ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrWhiteSpace(externalId))
                    return Unauthorized(ApiResponse.Failure("В токене отсутствует идентификатор пользователя", "UNAUTHORIZED"));

                if (!string.Equals(externalId, request.ExternalId?.Trim(), StringComparison.Ordinal))
                    return StatusCode(StatusCodes.Status403Forbidden,
                        ApiResponse.Failure("ExternalId запроса не совпадает с идентификатором токена", "FORBIDDEN"));

                // Не доверяем Roles/ExternalId из JSON: роли берутся только из claims,
                // прошедших JWT signature, issuer, audience и lifetime validation.
                var email = User.FindFirst("email")?.Value
                    ?? User.FindFirst(ClaimTypes.Email)?.Value
                    ?? string.Empty;
                var firstName = User.FindFirst("given_name")?.Value
                    ?? User.FindFirst(ClaimTypes.GivenName)?.Value
                    ?? string.Empty;
                var lastName = User.FindFirst("family_name")?.Value
                    ?? User.FindFirst(ClaimTypes.Surname)?.Value
                    ?? string.Empty;
                var roles = ExtractRolesFromClaims(User);

                var user = await _userService.GetUserByExternalIdAsync(externalId, ct);

                user ??= await _userService.RegisterExternalUserAsync(
                    externalId,
                    email,
                    firstName,
                    lastName,
                    roles,
                    ct);

                var fullName = string.Join(" ", new[] { user.FirstName, user.LastName }
                    .Where(value => !string.IsNullOrEmpty(value)));

                return Ok(ApiResponse.Success(new { userId = user.UserId, fullName }, "Авторизация успешна"));
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
                return StatusCode(StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure("Внутренняя ошибка при регистрации пользователя", "INTERNAL_ERROR"));
            }
        }

        private static IReadOnlyCollection<string> ExtractRolesFromClaims(ClaimsPrincipal principal)
        {
            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var claim in principal.FindAll("role").Concat(principal.FindAll(ClaimTypes.Role)))
            {
                if (claim.Value.StartsWith("ROLE_", StringComparison.OrdinalIgnoreCase))
                    roles.Add(claim.Value);
            }

            var realmAccess = principal.FindFirst("realm_access")?.Value;
            if (!string.IsNullOrWhiteSpace(realmAccess))
            {
                try
                {
                    using var document = JsonDocument.Parse(realmAccess);
                    if (document.RootElement.TryGetProperty("roles", out var roleArray) &&
                        roleArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var role in roleArray.EnumerateArray())
                        {
                            var value = role.GetString();
                            if (!string.IsNullOrWhiteSpace(value) &&
                                value.StartsWith("ROLE_", StringComparison.OrdinalIgnoreCase))
                            {
                                roles.Add(value);
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    // Некорректный optional claim не должен менять результат JWT-аутентификации.
                }
            }

            return roles.ToArray();
        }
    }
}
