using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.Core.DTOs.Moderation;
using Messenger.Core.Interfaces;
using Messenger.Infrastructure.Interfaces.Administration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;
using Messenger.Core.Responses;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для модерации: жалобы, муты и снятие мутов.
    /// </summary>
    [ApiController]
    [Route("api/v{version:apiVersion}/moderation")]
    [ApiVersion("1.0")]
    [Authorize]
    [Produces("application/json")]
    [Consumes("application/json")]
    [Tags("Moderation")]
    public class ModerationController : ControllerBase
    {
        private readonly IModerationWorkflowService _moderationWorkflowService;
        private readonly IAdminWorkflowService _adminWorkflowService;
        private readonly IUserService _userService;
        private readonly ILogger<ModerationController> _logger;

        public ModerationController(
            IModerationWorkflowService moderationWorkflowService,
            IAdminWorkflowService adminWorkflowService,
            IUserService userService,
            ILogger<ModerationController> logger)
        {
            _moderationWorkflowService = moderationWorkflowService;
            _adminWorkflowService = adminWorkflowService;
            _userService = userService;
            _logger = logger;
        }

        private async Task<Guid> EnsureAdminAsync(CancellationToken ct)
        {
            var userId = await User.GetCurrentUserIdAsync(_userService);
            await _adminWorkflowService.EnsureAdminAsync(userId, ct);

            return userId;
        }

        /// <summary>
        /// Отправить жалобу на сообщение.
        /// </summary>
        [HttpPost("report")]
        [EndpointName("ReportMessage")]
        [EndpointSummary("Пожаловаться на сообщение")]
        [EndpointDescription("Создаёт жалобу на указанное сообщение от имени текущего пользователя.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostReportAsync(
            [FromBody, Description("Данные жалобы на сообщение")] ReportMessageRequest request,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var report = await _moderationWorkflowService.ReportMessageAsync(userId, request, ct);

                return Ok(ApiResponse.Success(report));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при отправке жалобы");
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
                _logger.LogError(ex, "Ошибка при выполнении POST /moderation/report");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Выдать мут пользователю.
        /// </summary>
        [HttpPost("mute")]
        [EndpointName("MuteUser")]
        [EndpointSummary("Выдать мут пользователю")]
        [EndpointDescription("Ограничивает отправку сообщений пользователем. Требует роль администратора.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostMuteAsync(
            [FromBody, Description("Параметры мута пользователя")] MuteUserRequest request,
            CancellationToken ct = default)
        {
            try
            {
                var adminUserId = await EnsureAdminAsync(ct);
                var mute = await _moderationWorkflowService.MuteUserAsync(adminUserId, request, ct);

                return Ok(ApiResponse.Success(mute));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при выдаче мута");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (AdminForbiddenAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Failure(ex.Message, "FORBIDDEN"));
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
                _logger.LogError(ex, "Ошибка при выполнении POST /moderation/mute");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Снять мут с пользователя.
        /// </summary>
        [HttpPost("unmute/{muteId:guid}")]
        [EndpointName("UnmuteUser")]
        [EndpointSummary("Снять мут")]
        [EndpointDescription("Снимает активный мут по его идентификатору. Требует роль администратора.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostUnmuteAsync(
            [FromRoute, Description("Идентификатор мута (GUID)")] Guid muteId,
            CancellationToken ct = default)
        {
            try
            {
                var adminUserId = await EnsureAdminAsync(ct);
                await _moderationWorkflowService.UnmuteAsync(adminUserId, muteId, ct);

                return Ok(ApiResponse.Success());
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при снятии мута {MuteId}", muteId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (AdminForbiddenAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Failure(ex.Message, "FORBIDDEN"));
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
                _logger.LogError(ex, "Ошибка при выполнении POST /moderation/unmute/{MuteId}", muteId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

     }
}