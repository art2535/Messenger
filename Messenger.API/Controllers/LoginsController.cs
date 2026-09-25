using Asp.Versioning;
using Messenger.API.Responses;
using Messenger.API.Services;
using Messenger.Core.DTOs.Logins;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для управления входами в мессенджер
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Tags("Logins")]
    public class LoginsController : ControllerBase
    {
        private readonly ILoginService _loginService;
        private readonly IUserService _userService;

        public LoginsController(ILoginService loginService, IUserService userService)
        {
            _loginService = loginService;
            _userService = userService;
        }

        /// <summary>
        /// Получение истории входов текущего пользователя
        /// </summary>
        [HttpGet]
        [EndpointName("GetLogins")]
        [EndpointSummary("Получение истории входов текущего пользователя")]
        [EndpointDescription("Возвращает список всех сессий (входов) авторизованного пользователя. " +
            "Включает информацию о токене, IP-адресе, времени входа и статусе активности.")]
        [ProducesResponseType(typeof(GetLoginsSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetLoginsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                {
                    return error;
                }

                var logins = await _loginService.GetLoginsByUserIdAsync(user!.UserId, cancellationToken);

                return Ok(new GetLoginsSuccessResponse
                {
                    IsSuccess = true,
                    Data = logins
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Регистрация нового входа в систему
        /// </summary>
        [HttpPost]
        [EndpointName("CreateLogin")]
        [EndpointSummary("Регистрация нового входа в систему")]
        [EndpointDescription("Создаёт запись о новом входе пользователя (новая сессия). Вызывается после успешной аутентификации. Требуется действительный JWT-токен.")]
        [ProducesResponseType(typeof(CreateLoginSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> LoginAsync(
            [FromBody, Description("Данные новой сессии входа")] CreateLoginRequest request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                {
                    return error;
                }

                var login = new Login
                {
                    LoginId = Guid.NewGuid(),
                    UserId = user!.UserId,
                    Token = $"{request.Token.Substring(0, 20)}...",
                    IpAddress = request.IpAddress,
                    LoginTime = DateTime.Now,
                    Active = true
                };

                await _loginService.AddLoginAsync(login, cancellationToken);

                return Ok(new CreateLoginSuccessResponse
                {
                    IsSuccess = true,
                    Message = "Вход успешно добавлен"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Регистрация выхода из системы
        /// </summary>
        [HttpPatch]
        [EndpointName("Logout")]
        [EndpointSummary("Регистрация выхода из системы")]
        [EndpointDescription("Помечает текущую активную сессию пользователя как неактивную (выход). Вызывается при логауте. Устанавливает время выхода и деактивирует сессию.")]
        [ProducesResponseType(typeof(LogoutSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> LogoutAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                {
                    return error;
                }

                var logins = await _loginService.GetLoginsByUserIdAsync(user!.UserId, cancellationToken);

                var userLogout = logins.FirstOrDefault(l => l.UserId == user.UserId && l.Active == true);
                if (userLogout != null)
                {
                    userLogout.Token = string.Empty;
                    userLogout.Active = false;
                    userLogout.LogoutTime = DateTime.Now;

                    await _loginService.UpdateLoginAsync(userLogout, cancellationToken);
                }

                return Ok(new LogoutSuccessResponse
                {
                    IsSuccess = true,
                    Message = "Выход успешно обновлен"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }
    }
}
