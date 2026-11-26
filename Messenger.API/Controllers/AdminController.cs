using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.Core.Interfaces;
using Messenger.Infrastructure.Interfaces.Administration;
using Messenger.Core.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для администрирования системы: статистика, жалобы, муты, логи.
    /// </summary>
    [ApiController]
    [Route("api/v{version:apiVersion}/admin")]
    [ApiVersion("1.0")]
    [Authorize]
    [Produces("application/json")]
    [Consumes("application/json")]
    [Tags("Admin")]
    public class AdminController : ControllerBase
    {
        private readonly IAdminWorkflowService _adminWorkflowService;
        private readonly IMessageNotificationOutboxRecoveryService _outboxRecoveryService;
        private readonly IUserService _userService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            IAdminWorkflowService adminWorkflowService,
            IMessageNotificationOutboxRecoveryService outboxRecoveryService,
            IUserService userService,
            ILogger<AdminController> logger)
        {
            _adminWorkflowService = adminWorkflowService;
            _outboxRecoveryService = outboxRecoveryService;
            _userService = userService;
            _logger = logger;
        }

        private async Task<Guid> EnsureAdminAsync(CancellationToken ct)
        {
            var userId = await User.GetCurrentUserIdAsync(_userService);
            await _adminWorkflowService.EnsureAdminAsync(userId, ct);
            return userId;
        }

        private IActionResult ForbiddenResult(string message) =>
            StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Failure(message, "FORBIDDEN"));

        /// <summary>
        /// Проверить, является ли текущий пользователь администратором.
        /// </summary>
        [HttpGet("me")]
        [EndpointName("AdminMe")]
        [EndpointSummary("Проверить права администратора")]
        [EndpointDescription("Возвращает информацию о текущем пользователе и флаг IsAdmin.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetMeAsync(CancellationToken ct)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var adminInfo = await _adminWorkflowService.GetAdminInfoAsync(userId, ct);

                return Ok(ApiResponse.Success(adminInfo, "Права администратора проверены"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при проверке прав администратора");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /admin/me");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Восстановить сообщения из DeadLetter Outbox.
        /// </summary>
        [HttpPost("message-notification-outbox/dead-letter/recover")]
        [EndpointName("AdminRecoverMessageNotificationOutbox")]
        [EndpointSummary("Восстановить DeadLetter Outbox")]
        [EndpointDescription(
            "Возвращает ограниченное количество записей DeadLetter в очередь повторной доставки.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> RecoverMessageNotificationOutboxAsync(
            [FromQuery, Description("Максимальное количество записей")] int take = 100,
            CancellationToken ct = default)
        {
            try
            {
                await EnsureAdminAsync(ct);

                var recovered = await _outboxRecoveryService.RecoverDeadLettersAsync(
                    take,
                    ct);

                return Ok(ApiResponse.Success(
                    new { recovered },
                    "DeadLetter записи возвращены в очередь."));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Ошибка авторизации при восстановлении DeadLetter Outbox.");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (AdminForbiddenAccessException ex)
            {
                return ForbiddenResult(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Ошибка при восстановлении DeadLetter Outbox.");
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Получить статистику активности.
        /// </summary>
        [HttpGet("stats")]
        [EndpointName("AdminStats")]
        [EndpointSummary("Статистика активности")]
        [EndpointDescription("Возвращает агрегированную статистику по активности системы. Требует роль администратора.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetStatsAsync(CancellationToken ct)
        {
            try
            {
                await EnsureAdminAsync(ct);
                var stats = await _adminWorkflowService.GetActivityStatsAsync(ct);
                return Ok(ApiResponse.Success(stats, "Статистика получена"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении статистики");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (AdminForbiddenAccessException ex)
            {
                return ForbiddenResult(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /admin/stats");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR", meta: new { detail = ex.GetType().Name }));
            }
        }

        /// <summary>
        /// Получить список жалоб на рассмотрении.
        /// </summary>
        [HttpGet("reports")]
        [EndpointName("AdminReports")]
        [EndpointSummary("Список жалоб")]
        [EndpointDescription("Возвращает список жалоб, ожидающих рассмотрения. Требует роль администратора.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetReportsAsync(
            [FromQuery, Description("Максимальное количество записей")] int take = 50,
            CancellationToken ct = default)
        {
            try
            {
                await EnsureAdminAsync(ct);

                var list = await _adminWorkflowService.GetPendingReportsAsync(take, ct);
                if (list == null)
                {
                    return Ok(ApiResponse.Success(
                        Array.Empty<object>(),
                        "IModerationService не зарегистрирован",
                        meta: new { note = "IModerationService не зарегистрирован" }));
                }

                return Ok(ApiResponse.Success(list, meta: new { count = list.Count }));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении списка жалоб");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (AdminForbiddenAccessException ex)
            {
                return ForbiddenResult(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка при выполнении GET /admin/reports (таблицы могут отсутствовать)");
                return Ok(ApiResponse.Success(Array.Empty<object>(), message: ex.Message,
                    meta: new { error = ex.Message }));
            }
        }

        /// <summary>
        /// Рассмотреть жалобу.
        /// </summary>
        [HttpPost("reports/{reportId:guid}/review")]
        [EndpointName("AdminReviewReport")]
        [EndpointSummary("Рассмотреть жалобу")]
        [EndpointDescription("Отмечает жалобу как рассмотренную. Можно указать статус и комментарий.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostReviewReportAsync(
            [FromRoute, Description("Идентификатор жалобы")] Guid reportId,
            [FromBody, Description("Статус и комментарий по результатам рассмотрения")] ReviewReportBody? body,
            CancellationToken ct = default)
        {
            try
            {
                var adminUserId = await EnsureAdminAsync(ct);

                await _adminWorkflowService.ReviewReportAsync(
                    reportId,
                    adminUserId,
                    body?.Status ?? "reviewed",
                    body?.Note,
                    ct);
                return Ok(ApiResponse.Success(message: "Жалоба рассмотрена"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при рассмотрении жалобы {ReportId}", reportId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (AdminForbiddenAccessException ex)
            {
                return ForbiddenResult(ex.Message);
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
                _logger.LogError(ex, "Ошибка при выполнении POST /admin/reports/{ReportId}/review", reportId);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Получить активные муты.
        /// </summary>
        [HttpGet("mutes")]
        [EndpointName("AdminMutes")]
        [EndpointSummary("Список активных мутов")]
        [EndpointDescription("Возвращает список пользователей, находящихся под мутом. Требует роль администратора.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetMutesAsync(CancellationToken ct = default)
        {
            try
            {
                await EnsureAdminAsync(ct);

                var list = await _adminWorkflowService.GetActiveMutesAsync(ct);
                if (list == null)
                {
                    return Ok(ApiResponse.Success(
                        Array.Empty<object>(),
                        "IModerationService не зарегистрирован",
                        meta: new { note = "IModerationService не зарегистрирован" }));
                }

                return Ok(ApiResponse.Success(list, meta: new { count = list.Count }));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении активных мутов");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (AdminForbiddenAccessException ex)
            {
                return ForbiddenResult(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка при выполнении GET /admin/mutes");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Получить логи модерации.
        /// </summary>
        [HttpGet("logs")]
        [EndpointName("AdminLogs")]
        [EndpointSummary("Логи модерации")]
        [EndpointDescription("Возвращает последние записи логов модерации. Требует роль администратора.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetLogsAsync(
            [FromQuery, Description("Максимальное количество записей")] int take = 100,
            CancellationToken ct = default)
        {
            try
            {
                await EnsureAdminAsync(ct);

                var list = await _adminWorkflowService.GetLogsAsync(take, ct);
                if (list == null)
                {
                    return Ok(ApiResponse.Success(
                        Array.Empty<object>(),
                        "IModerationService не зарегистрирован",
                        meta: new { note = "IModerationService не зарегистрирован" }));
                }

                return Ok(ApiResponse.Success(list, meta: new { count = list.Count }));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении логов модерации");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (AdminForbiddenAccessException ex)
            {
                return ForbiddenResult(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка при выполнении GET /admin/logs");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Удалить жалобу.
        /// </summary>
        [HttpDelete("reports/{reportId:guid}")]
        [EndpointName("AdminDeleteReport")]
        [EndpointSummary("Удалить жалобу")]
        [EndpointDescription("Полностью удаляет жалобу из системы. Требует роль администратора.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteReportAsync(
            [FromRoute, Description("Идентификатор жалобы")] Guid reportId,
            CancellationToken ct = default)
        {
            try
            {
                var adminUserId = await EnsureAdminAsync(ct);

                await _adminWorkflowService.DeleteReportAsync(reportId, adminUserId, ct);
                return Ok(ApiResponse.Success(message: "Жалоба удалена"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при удалении жалобы {ReportId}", reportId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (AdminForbiddenAccessException ex)
            {
                return ForbiddenResult(ex.Message);
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
                _logger.LogError(ex, "Ошибка при выполнении DELETE /admin/reports/{ReportId}", reportId);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Удалить запись лога.
        /// </summary>
        [HttpDelete("logs/{logId:guid}")]
        [EndpointName("AdminDeleteLog")]
        [EndpointSummary("Удалить запись лога")]
        [EndpointDescription("Удаляет указанную запись из логов модерации. Требует роль администратора.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteLogAsync(
            [FromRoute, Description("Идентификатор записи лога")] Guid logId,
            CancellationToken ct = default)
        {
            try
            {
                await EnsureAdminAsync(ct);

                await _adminWorkflowService.DeleteLogAsync(logId, ct);
                return Ok(ApiResponse.Success(message: "Запись лога удалена"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при удалении лога {LogId}", logId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (AdminForbiddenAccessException ex)
            {
                return ForbiddenResult(ex.Message);
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
                _logger.LogError(ex, "Ошибка при выполнении DELETE /admin/logs/{LogId}", logId);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        public class ReviewReportBody
        {
            public string? Status { get; set; }
            public string? Note { get; set; }
        }

     }
}
