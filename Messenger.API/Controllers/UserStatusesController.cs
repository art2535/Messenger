using Asp.Versioning;
using Messenger.API.Responses;
using Messenger.API.Services;
using Messenger.Core.DTOs.UserStatuses;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для управления статусами пользователей
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [Tags("UserStatuses")]
    public class UserStatusesController : ControllerBase
    {
        private readonly IUserStatusService _userStatusService;
        private readonly IUserService _userService;

        public UserStatusesController(IUserStatusService userStatusService, IUserService userService)
        {
            _userStatusService = userStatusService;
            _userService = userService;
        }

        /// <summary>
        /// Получить статус текущего пользователя
        /// </summary>
        [HttpGet]
        [EndpointName("GetUserStatus")]
        [EndpointSummary("Получить статус текущего пользователя")]
        [EndpointDescription("Возвращает текущий статус авторизованного пользователя: онлайн/офлайн и время последней активности.")]
        [ProducesResponseType(typeof(GetUserStatusSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetUserStatusesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                {
                    return error;
                }

                var status = await _userStatusService.GetUserStatusByUserIdAsync(user!.UserId, cancellationToken);

                if (status == null)
                {
                    return NotFound(new
                    {
                        IsSuccess = false,
                        Error = "Статус пользователя не найден"
                    });
                }

                return Ok(new GetUserStatusSuccessResponse
                {
                    IsSuccess = true,
                    Data = status
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
        /// Обновить статус текущего пользователя
        /// </summary>
        [HttpPut]
        [EndpointName("UpdateUserStatus")]
        [EndpointSummary("Обновить статус текущего пользователя")]
        [EndpointDescription("Обновляет статус онлайн/офлайн для авторизованного пользователя. " +
            "Поле LastActivity автоматически устанавливается на текущее время сервера.")]
        [ProducesResponseType(typeof(UpdateUserStatusSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateStatusAsync(
            [FromBody, Description("Новый статус пользователя")] UpdateStatusRequest request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                {
                    return error;
                }

                var userStatus = new UserStatus
                {
                    UserId = user!.UserId,
                    Online = request.Online,
                    LastActivity = DateTime.Now
                };

                await _userStatusService.UpdateUserStatusAsync(userStatus, cancellationToken);

                return Ok(new UpdateUserStatusSuccessResponse
                {
                    IsSuccess = true,
                    Message = "Статус пользователя обновлен"
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
