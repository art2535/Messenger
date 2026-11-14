using Messenger.API.Services;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для администрирования
    /// </summary>
    [ApiController]
    [Route("api/v{version:apiVersion}/admin")]
    [Authorize]
    public class AdminController : ControllerBase
    {
        private readonly IAdminService _admin;
        private readonly IUserService _users;
        private readonly IServiceProvider _sp;
        private readonly ILogger<AdminController> _logger;

        public AdminController(IAdminService admin, IUserService users, IServiceProvider sp, ILogger<AdminController> logger)
        {
            _admin = admin;
            _users = users;
            _sp = sp;
            _logger = logger;
        }

        private IModerationService? Moderation => _sp.GetService<IModerationService>();

        private async Task<(User? user, bool isAdmin)> ResolveAsync(CancellationToken ct)
        {
            var (user, _) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _users);
            if (user != null)
            {
                var isAdmin = await _admin.IsSystemAdminAsync(user.UserId, ct);
                return (user, isAdmin);
            }

            var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (!string.IsNullOrEmpty(idClaim) && Guid.TryParse(idClaim, out var userId))
            {
                var byId = await _users.GetUserByIdAsync(userId, ct);
                if (byId != null)
                {
                    var isAdmin = await _admin.IsSystemAdminAsync(byId.UserId, ct);
                    return (byId, isAdmin);
                }
            }

            return (null, false);
        }

        private async Task<IActionResult?> EnsureAdminAsync(CancellationToken ct)
        {
            var (user, isAdmin) = await ResolveAsync(ct);
            if (user == null) return Unauthorized(new { error = "Не авторизован" });
            if (!isAdmin) return StatusCode(StatusCodes.Status403Forbidden, new { error = "Нужна роль «Администратор»" });
            return null;
        }

        [HttpGet("me")]
        public async Task<IActionResult> Me(CancellationToken ct)
        {
            try
            {
                var (user, isAdmin) = await ResolveAsync(ct);
                if (user == null)
                    return Unauthorized(new { isAdmin = false, error = "Не авторизован" });
                return Ok(new { isAdmin, userId = user.UserId, login = user.Login });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GET /admin/me failed");
                return StatusCode(500, new { isAdmin = false, error = ex.Message });
            }
        }

        [HttpGet("stats")]
        public async Task<IActionResult> Stats(CancellationToken ct)
        {
            try
            {
                var deny = await EnsureAdminAsync(ct);
                if (deny != null) return deny;
                var stats = await _admin.GetActivityStatsAsync(ct);
                return Ok(new { data = stats });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GET /admin/stats failed");
                return StatusCode(500, new { error = ex.Message, detail = ex.GetType().Name });
            }
        }

        [HttpGet("reports")]
        public async Task<IActionResult> Reports([FromQuery] int take = 50, CancellationToken ct = default)
        {
            try
            {
                var deny = await EnsureAdminAsync(ct);
                if (deny != null) return deny;

                var mod = Moderation;
                if (mod == null)
                    return Ok(new { data = Array.Empty<object>(), note = "IModerationService не зарегистрирован" });

                var list = await mod.GetPendingReportsAsync(take, ct);
                return Ok(new { data = list });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GET /admin/reports failed (tables may be missing)");
                return Ok(new { data = Array.Empty<object>(), error = ex.Message });
            }
        }

        [HttpPost("reports/{reportId:guid}/review")]
        public async Task<IActionResult> ReviewReport(Guid reportId, [FromBody] ReviewReportBody? body, CancellationToken ct = default)
        {
            try
            {
                var (user, isAdmin) = await ResolveAsync(ct);
                if (user == null) return Unauthorized();
                if (!isAdmin) return StatusCode(StatusCodes.Status403Forbidden);

                var mod = Moderation;
                if (mod == null)
                    return BadRequest(new { error = "Модерация не подключена" });

                await mod.ReviewReportAsync(reportId, user.UserId, body?.Status ?? "reviewed", body?.Note, ct);
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "POST /admin/reports/{ReportId}/review failed", reportId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("mutes")]
        public async Task<IActionResult> Mutes(CancellationToken ct = default)
        {
            try
            {
                var deny = await EnsureAdminAsync(ct);
                if (deny != null) return deny;

                var mod = Moderation;
                if (mod == null)
                    return Ok(new { data = Array.Empty<object>(), note = "IModerationService не зарегистрирован" });

                var list = await mod.GetActiveMutesAsync(ct);
                return Ok(new { data = list });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GET /admin/mutes failed");
                return Ok(new { data = Array.Empty<object>(), error = ex.Message });
            }
        }

        [HttpGet("logs")]
        public async Task<IActionResult> Logs([FromQuery] int take = 100, CancellationToken ct = default)
        {
            try
            {
                var deny = await EnsureAdminAsync(ct);
                if (deny != null) return deny;

                var mod = Moderation;
                if (mod == null)
                    return Ok(new { data = Array.Empty<object>(), note = "IModerationService не зарегистрирован" });

                var list = await mod.GetLogsAsync(take, ct);
                return Ok(new { data = list });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GET /admin/logs failed");
                return Ok(new { data = Array.Empty<object>(), error = ex.Message });
            }
        }


        [HttpDelete("reports/{reportId:guid}")]
        public async Task<IActionResult> DeleteReport(Guid reportId, CancellationToken ct = default)
        {
            try
            {
                var (user, isAdmin) = await ResolveAsync(ct);
                if (user == null) return Unauthorized();
                if (!isAdmin) return StatusCode(StatusCodes.Status403Forbidden);

                var mod = Moderation;
                if (mod == null)
                    return BadRequest(new { error = "Модерация не подключена" });

                await mod.DeleteReportAsync(reportId, user.UserId, ct);
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DELETE /admin/reports/{ReportId} failed", reportId);
                return StatusCode(500, new { error = ex.Message });
            }
        }



        [HttpDelete("logs/{logId:guid}")]
        public async Task<IActionResult> DeleteLog(Guid logId, CancellationToken ct = default)
        {
            try
            {
                var deny = await EnsureAdminAsync(ct);
                if (deny != null) return deny;

                var mod = Moderation;
                if (mod == null)
                    return BadRequest(new { error = "Модерация не подключена" });

                await mod.DeleteLogAsync(logId, ct);
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DELETE /admin/logs/{LogId} failed", logId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        public class ReviewReportBody
        {
            public string? Status { get; set; }
            public string? Note { get; set; }
        }
    }
}
