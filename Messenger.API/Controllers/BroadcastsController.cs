using Asp.Versioning;
using Messenger.API.Responses;
using Messenger.Core.DTOs.Broadcasts;
using Messenger.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;
using System.Security.Claims;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для управления рассылками сообщений
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/broadcasts")]
    [ApiVersion("1.0")]
    [Tags("Broadcasts")]
    public class BroadcastsController : ControllerBase
    {
        private readonly IBroadcastService _service;
        private readonly IUserService _userService;

        public BroadcastsController(IBroadcastService service, IUserService userService)
        {
            _service = service;
            _userService = userService;
        }

        private async Task<Guid> GetCurrentUserId()
        {
            var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(id) || !Guid.TryParse(id, out var extrnalId))
                throw new UnauthorizedAccessException("Не удалось определить пользователя");

            var user = await _userService.GetUserByExternalIdAsync(id);

            return user!.UserId;
        }

        /// <summary>
        /// Создать новую рассылку
        /// </summary>
        [HttpPost("create")]
        [EndpointName("CreateBroadcast")]
        [EndpointSummary("Создать новую рассылку")]
        [EndpointDescription("Позволяет создать массовую рассылку сообщений пользователям.")]
        [ProducesResponseType(typeof(BroadcastCreatedResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateBroadcastAsync(
            [FromBody, Description("Данные для создания рассылки")] CreateBroadcastRequest? request)
        {
            try
            {
                if (request == null)
                    return BadRequest("Тело запроса отсутствует");

                var senderId = await GetCurrentUserId();
                var response = await _service.CreateBroadcastAsync(request, senderId);

                return Created($"/api/broadcasts/{response.BroadcastId}", response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized();
            }
            catch (Exception)
            {
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = "Внутренняя ошибка при создании рассылки"
                });
            }
        }

        /// <summary>
        /// Получить статус рассылки
        /// </summary>
        [HttpGet("{id}")]
        [EndpointName("GetBroadcastStatus")]
        [EndpointSummary("Получить статус рассылки")]
        [EndpointDescription("Возвращает детальную информацию о рассылке по её ID.")]
        [ProducesResponseType(typeof(BroadcastSummaryDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetBroadcastStatusAsync(
            [Description("Уникальный идентификатор рассылки (GUID)")] Guid id)
        {
            try
            {
                var userId = await GetCurrentUserId();
                var isAdmin = User.IsInRole("Admin");

                var summary = await _service.GetBroadcastSummaryAsync(id, userId, isAdmin);

                if (summary == null)
                    return NotFound("Рассылка не найдена");

                return Ok(summary);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception)
            {
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = "Ошибка при получении статуса рассылки"
                });
            }
        }

        /// <summary>
        /// Отметить рассылку как прочитанную
        /// </summary>
        [HttpPost("{id}/read")]
        [EndpointName("MarkBroadcastAsRead")]
        [EndpointSummary("Отметить рассылку как прочитанную")]
        [EndpointDescription("Устанавливает статус «Прочитано» для текущего пользователя в конкретной рассылке.")]
        [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> MarkAsReadAsync(
            [Description("Уникальный идентификатор рассылки (GUID)")] Guid id)
        {
            try
            {
                var userId = await GetCurrentUserId();
                var result = await _service.MarkAsReadAsync(id, userId);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception)
            {
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = "Ошибка при отметке прочтения"
                });
            }
        }

        /// <summary>
        /// Получить список моих рассылок
        /// </summary>
        [HttpGet("my")]
        [EndpointName("GetMyBroadcasts")]
        [EndpointSummary("Получить список моих рассылок")]
        [EndpointDescription("Возвращает список рассылок, адресованных текущему пользователю.")]
        [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetMyBroadcastsAsync(
            [FromQuery, Description("Фильтр: только непрочитанные")] bool unreadOnly = true)
        {
            try
            {
                var userId = await GetCurrentUserId();
                var broadcasts = await _service.GetMyBroadcastsAsync(userId, unreadOnly);
                return Ok(broadcasts);
            }
            catch (Exception)
            {
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = "Ошибка при получении ваших рассылок"
                });
            }
        }
    }
}