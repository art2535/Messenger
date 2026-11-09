using Asp.Versioning;
using Messenger.API.Responses;
using Messenger.API.Services;
using Messenger.Core.DTOs.AccountSettings;
using Messenger.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Настройки аккаунта: уведомления, внешний вид, конфиденциальность
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/account-settings")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Tags("AccountSettings")]
    public class AccountSettingsController : ControllerBase
    {
        private readonly IAccountSettingsService _accountSettingsService;
        private readonly IUserService _userService;

        public AccountSettingsController(
            IAccountSettingsService accountSettingsService,
            IUserService userService)
        {
            _accountSettingsService = accountSettingsService;
            _userService = userService;
        }

        /// <summary>
        /// Получить настройки текущего пользователя
        /// </summary>
        [HttpGet]
        [EndpointName("GetAccountSettings")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                    return error;

                var data = await _accountSettingsService.GetByUserIdAsync(user!.UserId, cancellationToken);
                return Ok(new { isSuccess = true, data });
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
        /// Обновить настройки текущего пользователя
        /// </summary>
        [HttpPut]
        [EndpointName("UpdateAccountSettings")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateAsync([FromBody] UpdateAccountSettingsRequest request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                    return error;

                var data = await _accountSettingsService.UpdateAsync(user!.UserId, request, cancellationToken);
                return Ok(new { isSuccess = true, data });
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
