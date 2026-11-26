using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.Core.DTOs.AccountSettings;
using Messenger.Core.Interfaces;
using Messenger.Infrastructure.Interfaces.Users;
using Messenger.Core.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Настройки аккаунта: уведомления, внешний вид, конфиденциальность.
    /// </summary>
    [ApiController]
    [Route("api/v{version:apiVersion}/account-settings")]
    [ApiVersion("1.0")]
    [Authorize]
    [Produces("application/json")]
    [Consumes("application/json")]
    [Tags("AccountSettings")]
    public class AccountSettingsController : ControllerBase
    {
        private readonly IAccountSettingsWorkflowService _accountSettingsWorkflowService;
        private readonly IUserService _userService;
        private readonly ILogger<AccountSettingsController> _logger;

        public AccountSettingsController(
            IAccountSettingsWorkflowService accountSettingsWorkflowService,
            IUserService userService,
            ILogger<AccountSettingsController> logger)
        {
            _accountSettingsWorkflowService = accountSettingsWorkflowService;
            _userService = userService;
            _logger = logger;
        }

        /// <summary>
        /// Получить настройки текущего пользователя.
        /// </summary>
        [HttpGet]
        [EndpointName("GetAccountSettings")]
        [EndpointSummary("Получить настройки текущего пользователя")]
        [EndpointDescription("Возвращает текущие настройки аккаунта авторизованного пользователя: уведомления, внешний вид и конфиденциальность.")]
        [ProducesResponseType(typeof(ApiResponse<AccountSettingsDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAccountSettingsAsync(CancellationToken ct)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var settings = await _accountSettingsWorkflowService.GetByUserIdAsync(userId, ct);

                return Ok(ApiResponse.Success(settings, "Настройки аккаунта получены"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении настроек аккаунта");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении настроек аккаунта");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Обновить настройки текущего пользователя.
        /// </summary>
        [HttpPut]
        [EndpointName("UpdateAccountSettings")]
        [EndpointSummary("Обновить настройки текущего пользователя")]
        [EndpointDescription("Обновляет настройки аккаунта авторизованного пользователя. Можно передать только те поля, которые нужно изменить.")]
        [ProducesResponseType(typeof(ApiResponse<AccountSettingsDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PutUpdateAccountSettingsAsync(
            [FromBody, Description("Новые значения настроек аккаунта")] UpdateAccountSettingsRequest request,
            CancellationToken ct)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var settings = await _accountSettingsWorkflowService.UpdateAsync(userId, request, ct);

                return Ok(ApiResponse.Success(settings, "Настройки аккаунта обновлены"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при обновлении настроек аккаунта");
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
                _logger.LogError(ex, "Ошибка при обновлении настроек аккаунта");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }
    }
}
