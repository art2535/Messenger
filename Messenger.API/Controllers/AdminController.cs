using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.Core.Interfaces;
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
        private readonly IAdminService _adminService;
        private readonly IUserService _userService;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            IAdminService adminService,
            IUserService userService,
            IServiceProvider serviceProvider,
            ILogger<AdminController> logger)
        {
            _adminService = adminService;
            _userService = userService;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        private IModerationService? Moderation => _serviceProvider.GetService<IModerationService>();

        private async Task<(Guid userId, bool isAdmin)> ResolveAdminAsync(CancellationToken ct)
        {
            var userId = await User.GetCurrentUserIdAsync(_userService);
            var isAdmin = await _adminService.IsSystemAdminAsync(userId, ct);
            return (userId, isAdmin);
        }

        private async Task<Guid> EnsureAdminAsync(CancellationToken ct)
        {
            var (userId, isAdmin) = await ResolveAdminAsync(ct);
            if (!isAdmin)
                throw new ForbiddenAccessException("Нужна роль «Администратор»");

            return userId;
        }

        /// <summary>
        /// Проверить, является ли текущий пользователь администратором.
        /// </summary>
        [HttpGet("me")]
        [EndpointName("AdminMe")]
        [EndpointSummary("Проверить права администратора")]
        [EndpointDescription("Возвращает информацию о текущем пользователе и флаг IsAdmin.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetMeAsync(CancellationToken ct)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var isAdmin = await _adminService.IsSystemAdminAsync(userId, ct);
                var user = await _userService.GetUserByIdAsync(userId, ct);

                return Ok(new
                {
                    IsAdmin = isAdmin,
                    UserId = userId,
                    Login = user?.Login
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при проверке прав администратора");
                return Unauthorized(new
                {
                    IsAdmin = false,
                    Error = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /admin/me");
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    IsAdmin = false,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Получить статистику активности.
        /// </summary>
        [HttpGet("stats")]
        [EndpointName("AdminStats")]
        [EndpointSummary("Статистика активности")]
        [EndpointDescription("Возвращает агрегированную статистику по активности системы. Требует роль администратора.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetStatsAsync(CancellationToken ct)
        {
            try
            {
                await EnsureAdminAsync(ct);
                var stats = await _adminService.GetActivityStatsAsync(ct);
                return Ok(new
                {
                    Data = stats
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении статистики");
                return Unauthorized(new
                {
                    Error = ex.Message
                });
            }
            catch (ForbiddenAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /admin/stats");
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    Error = ex.Message,
                    Detail = ex.GetType().Name
                });
            }
        }

        /// <summary>
        /// Получить список жалоб на рассмотрении.
        /// </summary>
        [HttpGet("reports")]
        [EndpointName("AdminReports")]
        [EndpointSummary("Список жалоб")]
        [EndpointDescription("Возвращает список жалоб, ожидающих рассмотрения. Требует роль администратора.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetReportsAsync(
            [FromQuery, Description("Максимальное количество записей")] int take = 50,
            CancellationToken ct = default)
        {
            try
            {
                await EnsureAdminAsync(ct);

                var mod = Moderation;
                if (mod == null)
                    return Ok(new
                    {
                        Data = Array.Empty<object>(),
                        Note = "IModerationService не зарегистрирован"
                    });

                var list = await mod.GetPendingReportsAsync(take, ct);
                return Ok(new
                {
                    Data = list
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении списка жалоб");
                return Unauthorized(new
                {
                    Error = ex.Message
                });
            }
            catch (ForbiddenAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка при выполнении GET /admin/reports (таблицы могут отсутствовать)");
                return Ok(new
                {
                    Data = Array.Empty<object>(),
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Рассмотреть жалобу.
        /// </summary>
        [HttpPost("reports/{reportId:guid}/review")]
        [EndpointName("AdminReviewReport")]
        [EndpointSummary("Рассмотреть жалобу")]
        [EndpointDescription("Отмечает жалобу как рассмотренную. Можно указать статус и комментарий.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostReviewReportAsync(
            [FromRoute, Description("Идентификатор жалобы")] Guid reportId,
            [FromBody, Description("Статус и комментарий по результатам рассмотрения")] ReviewReportBody? body,
            CancellationToken ct = default)
        {
            try
            {
                var adminUserId = await EnsureAdminAsync(ct);

                var mod = Moderation;
                if (mod == null)
                    return BadRequest(new
                    {
                        Error = "Модерация не подключена"
                    });

                await mod.ReviewReportAsync(reportId, adminUserId, body?.Status ?? "reviewed", body?.Note, ct);
                return Ok(new
                {
                    IsSuccess = true
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при рассмотрении жалобы {ReportId}", reportId);
                return Unauthorized(new
                {
                    Error = ex.Message
                });
            }
            catch (ForbiddenAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new
                {
                    Error = ex.Message
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    Error = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении POST /admin/reports/{ReportId}/review", reportId);
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Получить активные муты.
        /// </summary>
        [HttpGet("mutes")]
        [EndpointName("AdminMutes")]
        [EndpointSummary("Список активных мутов")]
        [EndpointDescription("Возвращает список пользователей, находящихся под мутом. Требует роль администратора.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetMutesAsync(CancellationToken ct = default)
        {
            try
            {
                await EnsureAdminAsync(ct);

                var mod = Moderation;
                if (mod == null)
                    return Ok(new
                    {
                        Data = Array.Empty<object>(),
                        Note = "IModerationService не зарегистрирован"
                    });

                var list = await mod.GetActiveMutesAsync(ct);
                return Ok(new
                {
                    Data = list
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении активных мутов");
                return Unauthorized(new
                {
                    Error = ex.Message
                });
            }
            catch (ForbiddenAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка при выполнении GET /admin/mutes");
                return Ok(new
                {
                    Data = Array.Empty<object>(),
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Получить логи модерации.
        /// </summary>
        [HttpGet("logs")]
        [EndpointName("AdminLogs")]
        [EndpointSummary("Логи модерации")]
        [EndpointDescription("Возвращает последние записи логов модерации. Требует роль администратора.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetLogsAsync(
            [FromQuery, Description("Максимальное количество записей")] int take = 100,
            CancellationToken ct = default)
        {
            try
            {
                await EnsureAdminAsync(ct);

                var mod = Moderation;
                if (mod == null)
                    return Ok(new
                    {
                        Data = Array.Empty<object>(),
                        Note = "IModerationService не зарегистрирован"
                    });

                var list = await mod.GetLogsAsync(take, ct);
                return Ok(new
                {
                    Data = list
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении логов модерации");
                return Unauthorized(new
                {
                    Error = ex.Message
                });
            }
            catch (ForbiddenAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка при выполнении GET /admin/logs");
                return Ok(new
                {
                    Data = Array.Empty<object>(),
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Удалить жалобу.
        /// </summary>
        [HttpDelete("reports/{reportId:guid}")]
        [EndpointName("AdminDeleteReport")]
        [EndpointSummary("Удалить жалобу")]
        [EndpointDescription("Полностью удаляет жалобу из системы. Требует роль администратора.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteReportAsync(
            [FromRoute, Description("Идентификатор жалобы")] Guid reportId,
            CancellationToken ct = default)
        {
            try
            {
                var adminUserId = await EnsureAdminAsync(ct);

                var mod = Moderation;
                if (mod == null)
                {
                    return BadRequest(new
                    {
                        Error = "Модерация не подключена"
                    });
                }

                await mod.DeleteReportAsync(reportId, adminUserId, ct);
                return Ok(new
                {
                    IsSuccess = true
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при удалении жалобы {ReportId}", reportId);
                return Unauthorized(new
                {
                    Error = ex.Message
                });
            }
            catch (ForbiddenAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new
                {
                    Error = ex.Message
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    Error = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении DELETE /admin/reports/{ReportId}", reportId);
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Удалить запись лога.
        /// </summary>
        [HttpDelete("logs/{logId:guid}")]
        [EndpointName("AdminDeleteLog")]
        [EndpointSummary("Удалить запись лога")]
        [EndpointDescription("Удаляет указанную запись из логов модерации. Требует роль администратора.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteLogAsync(
            [FromRoute, Description("Идентификатор записи лога")] Guid logId,
            CancellationToken ct = default)
        {
            try
            {
                await EnsureAdminAsync(ct);

                var mod = Moderation;
                if (mod == null)
                    return BadRequest(new
                    {
                        Error = "Модерация не подключена"
                    });

                await mod.DeleteLogAsync(logId, ct);
                return Ok(new
                {
                    IsSuccess = true
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при удалении лога {LogId}", logId);
                return Unauthorized(new
                {
                    Error = ex.Message
                });
            }
            catch (ForbiddenAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new
                {
                    Error = ex.Message
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    Error = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении DELETE /admin/logs/{LogId}", logId);
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    Error = ex.Message
                });
            }
        }

        public class ReviewReportBody
        {
            public string? Status { get; set; }
            public string? Note { get; set; }
        }

        private sealed class ForbiddenAccessException(string message) : Exception(message);
    }
}