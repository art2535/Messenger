using Messenger.Application.Common;
using Messenger.Application.DTOs.Responses;
﻿using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.Application.DTOs.Logins;
using Messenger.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для управления входами в мессенджер.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/login")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [Tags("Logins")]
    public class LoginsController : ControllerBase
    {
        private readonly ILoginService _loginService;
        private readonly IUserService _userService;
        private readonly ILogger<LoginsController> _logger;

        public LoginsController(
            ILoginService loginService,
            IUserService userService,
            ILogger<LoginsController> logger)
        {
            _loginService = loginService;
            _userService = userService;
            _logger = logger;
        }

        /// <summary>
        /// Получение истории входов текущего пользователя.
        /// </summary>
        [HttpGet]
        [EndpointName("GetLogins")]
        [EndpointSummary("Получение истории входов текущего пользователя")]
        [EndpointDescription("Возвращает список всех сессий (входов) авторизованного пользователя. " +
            "Включает информацию о токене, IP-адресе, времени входа и статусе активности.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetLoginsAsync(CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);

                var logins = await _loginService.GetLoginsByUserIdAsync(userId, ct);
                var data = logins
                    .Where(l => l.Active)
                    .OrderByDescending(l => l.LoginTime)
                    .Select(l => new
                    {
                        LoginId = l.LoginId,
                        IpAddress = l.IpAddress,
                        LoginTime = l.LoginTime,
                        LogoutTime = l.LogoutTime,
                        Active = l.Active
                    })
                    .ToList();

                return Ok(ApiResponse.Success(data));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении истории входов");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /login");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Принудительное завершение сессии по ключу (вызывается фоновым сервисом Web при истечении/отзыве).
        /// </summary>
        [HttpPost("session/{sessionKey}/expire")]
        [AllowAnonymous]
        [EndpointName("ExpireLoginSession")]
        [EndpointSummary("Завершить сессию по ключу")]
        [EndpointDescription("Принудительно завершает сессию по идентификатору ключа.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostExpireSessionAsync(
            [FromRoute, Description("Ключ сессии")] string sessionKey,
            CancellationToken ct = default)
        {
            try
            {
                var expired = await _loginService.ExpireSessionAsync(sessionKey, ct);

                return Ok(ApiResponse.Success(new { expired }, expired ? "Сессия завершена" : "Сессия не найдена"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении POST /login/session/{SessionKey}/expire", sessionKey);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Регистрация нового входа в систему.
        /// </summary>
        [HttpPost]
        [EndpointName("CreateLogin")]
        [EndpointSummary("Регистрация нового входа в систему")]
        [EndpointDescription("Создаёт запись о новом входе пользователя (новая сессия). Вызывается после успешной аутентификации. Требуется действительный JWT-токен.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostLoginAsync(
            [FromBody, Description("Данные новой сессии входа")] CreateLoginRequest request,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);

                var rawToken = string.IsNullOrWhiteSpace(request.Token)
                    ? Guid.NewGuid().ToString("N")
                    : request.Token.Trim();

                var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

                var loginId = Guid.NewGuid();
                var login = new CreateLoginDto
                {
                    LoginId = loginId,
                    UserId = userId,
                    TokenHash = tokenHash,
                    IpAddress = request.IpAddress,
                    LoginTime = UtcDateTime.Now,
                    Active = true
                };

                await _loginService.AddLoginAsync(login, ct);

                return Ok(ApiResponse.Success(new { loginId, tokenHash }, "Вход успешно добавлен"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при регистрации входа");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
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
                _logger.LogError(ex, "Ошибка при выполнении POST /login");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Регистрация выхода из системы.
        /// </summary>
        [HttpPatch]
        [EndpointName("Logout")]
        [EndpointSummary("Регистрация выхода из системы")]
        [EndpointDescription("Помечает текущую активную сессию пользователя как неактивную (выход). Вызывается при логауте. Устанавливает время выхода и деактивирует сессию.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PatchLogoutAsync(CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);

                var logins = await _loginService.GetLoginsByUserIdAsync(userId, ct);

                var userLogout = logins.FirstOrDefault(l => l.UserId == userId && l.Active == true);
                if (userLogout != null)
                {
                    await _loginService.CloseLoginAsync(userId, userLogout.LoginId, ct);
                }

                return Ok(ApiResponse.Success(message: "Выход успешно обновлен"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при выходе из системы");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении PATCH /login");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Завершить конкретную сессию.
        /// </summary>
        [HttpDelete("{loginId:guid}")]
        [EndpointName("RevokeLogin")]
        [EndpointSummary("Завершить сессию")]
        [EndpointDescription("Завершает указанную сессию авторизованного пользователя.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteRevokeLoginAsync(
            [FromRoute, Description("Идентификатор сессии (GUID)")] Guid loginId,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);

                var ok = await _loginService.RevokeLoginAndUpdateStatusAsync(userId, loginId, ct);
                if (!ok)
                {
                    return NotFound(ApiResponse.Failure("Сессия не найдена или уже завершена", "NOT_FOUND"));
                }

                return Ok(ApiResponse.Success(message: "Сессия завершена"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при завершении сессии {LoginId}", loginId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении DELETE /login/{LoginId}", loginId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Завершить все сессии кроме указанной.
        /// </summary>
        [HttpPost("revoke-others")]
        [EndpointName("RevokeOtherLogins")]
        [EndpointSummary("Завершить другие сессии")]
        [EndpointDescription("Завершает все активные сессии пользователя, кроме указанной в параметре keepLoginId.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostRevokeOthersAsync(
            [FromQuery, Description("Идентификатор сессии, которую нужно сохранить")] Guid? keepLoginId = null,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService, ct);

                var revokedCount = await _loginService.RevokeOtherLoginsAsync(userId, keepLoginId, ct);

                return Ok(ApiResponse.Success(new { revoked = revokedCount }, "Другие сессии завершены"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при завершении других сессий");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении POST /login/revoke-others");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Проверка активности сессии по ключу UserTokenStore (для фонового TokenRefresh на Web).
        /// </summary>
        [HttpGet("session/{sessionKey}/active")]
        [AllowAnonymous]
        [EndpointName("IsLoginSessionActive")]
        [EndpointSummary("Проверить активность сессии")]
        [EndpointDescription("Проверяет, активна ли сессия по ключу.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetIsSessionActiveAsync(
            [FromRoute, Description("Ключ сессии")] string sessionKey,
            CancellationToken ct = default)
        {
            try
            {
                var active = await _loginService.IsSessionKeyActiveAsync(sessionKey, ct);

                return Ok(ApiResponse.Success(new { active = active ?? true, known = active.HasValue }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /login/session/{SessionKey}/active", sessionKey);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }
    }
}