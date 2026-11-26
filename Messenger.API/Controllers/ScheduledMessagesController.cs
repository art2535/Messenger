using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.Core.DTOs.Messages;
using Messenger.Core.Interfaces;
using Messenger.Infrastructure.Interfaces.Messages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;
using Messenger.Core.Responses;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Запланированные (отложенные) сообщения.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/scheduled-messages")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Tags("ScheduledMessages")]
    public class ScheduledMessagesController : ControllerBase
    {
        private readonly IScheduledMessageWorkflowService _scheduledWorkflowService;
        private readonly IUserService _userService;
        private readonly ILogger<ScheduledMessagesController> _logger;

        public ScheduledMessagesController(
            IScheduledMessageWorkflowService scheduledWorkflowService,
            IUserService userService,
            ILogger<ScheduledMessagesController> logger)
        {
            _scheduledWorkflowService = scheduledWorkflowService;
            _userService = userService;
            _logger = logger;
        }

        /// <summary>
        /// Запланировать сообщение в чат (текст + опционально файлы).
        /// </summary>
        [HttpPost("{chatId:guid}")]
        [Consumes("multipart/form-data", "application/json")]
        [EndpointName("ScheduleMessage")]
        [EndpointSummary("Запланировать сообщение в чат")]
        [EndpointDescription("Создаёт отложенное сообщение в указанном чате с опциональным прикреплением файлов.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostScheduleAsync(
            [Description("Идентификатор чата")] Guid chatId,
            [FromForm] string? messageText,
            [FromForm] DateTimeOffset? scheduledAt,
            [FromForm] string? replyToMessageId,
            [FromForm] IFormFile[]? files,
            CancellationToken ct = default)
        {
            try
            {
                var user = await User.GetCurrentUserAsync(_userService);

                var dto = await _scheduledWorkflowService.ScheduleAsync(
                    user,
                    chatId,
                    messageText,
                    scheduledAt,
                    replyToMessageId,
                    files,
                    ct);

                return Ok(ApiResponse.Success(dto));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при планировании сообщения");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (ScheduledMessageChatNotFoundException ex)
            {
                return NotFound(ApiResponse.Failure(ex.Message, "NOT_FOUND"));
            }
            catch (ScheduledMessageForbiddenException ex)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    ApiResponse.Failure(ex.Message, "FORBIDDEN"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Schedule message failed for chat {ChatId}", chatId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Список всех ожидающих запланированных сообщений текущего пользователя.
        /// </summary>
        [HttpGet]
        [EndpointName("ListMineScheduledMessages")]
        [EndpointSummary("Список ожидающих запланированных сообщений")]
        [EndpointDescription("Возвращает все отложенные сообщения текущего пользователя, которые ещё не были отправлены.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetListMineAsync(CancellationToken ct = default)
        {
            try
            {
                var user = await User.GetCurrentUserAsync(_userService);

                var dtos = await _scheduledWorkflowService.GetPendingByUserAsync(user, ct);

                return Ok(ApiResponse.Success(dtos));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении списка запланированных сообщений");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении списка запланированных сообщений пользователя");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Запланированные сообщения в конкретном чате (только свои pending).
        /// </summary>
        [HttpGet("chat/{chatId:guid}")]
        [EndpointName("ListScheduledMessagesByChat")]
        [EndpointSummary("Запланированные сообщения в чате")]
        [EndpointDescription("Возвращает отложенные сообщения текущего пользователя в указанном чате.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetListByChatAsync(
            [Description("Идентификатор чата")] Guid chatId,
            CancellationToken ct = default)
        {
            try
            {
                var user = await User.GetCurrentUserAsync(_userService);

                var dtos = await _scheduledWorkflowService.GetPendingByChatAsync(user, chatId, ct);

                return Ok(ApiResponse.Success(dtos));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении запланированных сообщений чата {ChatId}", chatId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении запланированных сообщений чата {ChatId}", chatId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Обновить текст или время запланированного сообщения.
        /// </summary>
        [HttpPut("{scheduledMessageId:guid}")]
        [Consumes("application/json")]
        [EndpointName("UpdateScheduledMessage")]
        [EndpointSummary("Обновить запланированное сообщение")]
        [EndpointDescription("Изменяет текст или время отправки ранее запланированного сообщения.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PutUpdateAsync(
            [Description("Идентификатор запланированного сообщения")] Guid scheduledMessageId,
            [FromBody] UpdateScheduledMessageRequest request,
            CancellationToken ct = default)
        {
            try
            {
                var user = await User.GetCurrentUserAsync(_userService);

                var dto = await _scheduledWorkflowService.UpdateAsync(
                    user,
                    scheduledMessageId,
                    request,
                    ct);

                if (dto == null)
                {
                    return NotFound(ApiResponse.Failure(
                        "Запланированное сообщение не найдено",
                        "NOT_FOUND"));
                }

                return Ok(ApiResponse.Success(dto));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при обновлении запланированного сообщения {Id}", scheduledMessageId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update scheduled {Id} failed", scheduledMessageId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Отменить запланированную отправку.
        /// </summary>
        [HttpDelete("{scheduledMessageId:guid}")]
        [EndpointName("CancelScheduledMessage")]
        [EndpointSummary("Отменить запланированную отправку")]
        [EndpointDescription("Удаляет отложенное сообщение из очереди на отправку.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteCancelAsync(
            [Description("Идентификатор запланированного сообщения")] Guid scheduledMessageId,
            CancellationToken ct = default)
        {
            try
            {
                var user = await User.GetCurrentUserAsync(_userService);

                var ok = await _scheduledWorkflowService.CancelAsync(user, scheduledMessageId, ct);
                if (!ok)
                {
                    return NotFound(ApiResponse.Failure("Сообщение не найдено или уже отправлено/отменено", "NOT_FOUND"));
                }

                 return Ok(ApiResponse.Success(message: "Запланированное сообщение успешно отменено"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при отмене запланированного сообщения {Id}", scheduledMessageId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cancel scheduled {Id} failed", scheduledMessageId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }
    }
}