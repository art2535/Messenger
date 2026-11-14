using Messenger.Core.DTOs.Moderation;
using Messenger.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Messenger.API.Controllers
{
    [ApiController]
    [Route("api/v{version:apiVersion}/moderation")]
    [Authorize]
    public class ModerationController : ControllerBase
    {
        private readonly IModerationService _moderation;
        private readonly IAdminService _admin;
        private readonly IUserService _users;

        public ModerationController(IModerationService moderation, IAdminService admin, IUserService users)
        {
            _moderation = moderation;
            _admin = admin;
            _users = users;
        }

        private async Task<Guid?> CurrentUserId(CancellationToken ct)
        {
            var externalId = User.FindFirstValue("sub")
                          ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(externalId))
            {
                var byExt = await _users.GetUserByExternalIdAsync(externalId);
                if (byExt != null) return byExt.UserId;
            }
            // Fallback: NameIdentifier = UserId (Guid) — JwtService
            if (!string.IsNullOrEmpty(externalId) && Guid.TryParse(externalId, out var userId))
            {
                var byId = await _users.GetUserByIdAsync(userId, ct);
                if (byId != null) return byId.UserId;
            }
            return null;
        }

        [HttpPost("report")]
        public async Task<IActionResult> Report([FromBody] ReportMessageRequest request, CancellationToken ct)
        {
            var uid = await CurrentUserId(ct);
            if (uid == null) return Unauthorized();
            try
            {
                var r = await _moderation.ReportMessageAsync(uid.Value, request, ct);
                return Ok(new { data = r });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("mute")]
        public async Task<IActionResult> Mute([FromBody] MuteUserRequest request, CancellationToken ct)
        {
            var uid = await CurrentUserId(ct);
            if (uid == null) return Unauthorized();
            if (!await _admin.IsSystemAdminAsync(uid.Value, ct))
                return Forbid();
            try
            {
                var m = await _moderation.MuteUserAsync(uid.Value, request, ct);
                return Ok(new { data = m });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("unmute/{muteId:guid}")]
        public async Task<IActionResult> Unmute(Guid muteId, CancellationToken ct)
        {
            var uid = await CurrentUserId(ct);
            if (uid == null) return Unauthorized();
            if (!await _admin.IsSystemAdminAsync(uid.Value, ct))
                return Forbid();
            await _moderation.UnmuteAsync(uid.Value, muteId, ct);
            return Ok(new { success = true });
        }
    }
}
