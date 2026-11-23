using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.Core.DTOs.Broadcasts;
using Messenger.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для управления рассылками сообщений.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/broadcasts")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [Tags("Broadcasts")]
    public class BroadcastsController : ControllerBase
    {
        private readonly IBroadcastService _broadcastService;
        private readonly IUserService _userService;
        private readonly ILogger<BroadcastsController> _logger;

        public BroadcastsController(
            IBroadcastService broadcastService,
            IUserService userService,
            ILogger<BroadcastsController> logger)
        {
            _broadcastService = broadcastService;
            _userService = userService;
            _logger = logger;
        }

        /// <summary>
        /// Создать новую рассылку.
        /// </summary>
        [HttpPost("create")]
        [EndpointName("CreateBroadcast")]
        [EndpointSummary("Создать новую рассылку")]
        [EndpointDescription("Позволяет создать массовую рассылку сообщений пользователям.")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostCreateBroadcastAsync(
            [FromBody, Description("Данные для создания рассылки")] CreateBroadcastRequest? request,
            CancellationToken ct = default)
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new
                    {
                        IsSuccess = false,
                        Error = "Тело запроса отсутствует"
                    });
                }

                var senderId = await User.GetCurrentUserIdAsync(_userService);
                var created = await _broadcastService.CreateBroadcastAsync(request, senderId);

                return Created($"/api/broadcasts/{created.BroadcastId}", new
                {
                    IsSuccess = true,
                    Data = created
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при создании рассылки");
                return Unauthorized(new
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении POST /broadcasts/create");
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    IsSuccess = false,
                    Error = "Внутренняя ошибка при создании рассылки"
                });
            }
        }

        /// <summary>
        /// Получить статус рассылки.
        /// </summary>
        [HttpGet("{id:guid}")]
        [EndpointName("GetBroadcastStatus")]
        [EndpointSummary("Получить статус рассылки")]
        [EndpointDescription("Возвращает детальную информацию о рассылке по её ID.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetBroadcastStatusAsync(
            [FromRoute, Description("Уникальный идентификатор рассылки (GUID)")] Guid id,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var isAdmin = User.IsInRole("Admin");

                var summary = await _broadcastService.GetBroadcastSummaryAsync(id, userId, isAdmin);

                if (summary == null)
                {
                    return NotFound(new
                    {
                        IsSuccess = false,
                        Error = "Рассылка не найдена"
                    });
                }

                return Ok(new
                {
                    IsSuccess = true,
                    Data = summary
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка доступа при получении статуса рассылки {BroadcastId}", id);
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /broadcasts/{BroadcastId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    IsSuccess = false,
                    Error = "Ошибка при получении статуса рассылки"
                });
            }
        }

        /// <summary>
        /// Отметить рассылку как прочитанную.
        /// </summary>
        [HttpPost("{id:guid}/read")]
        [EndpointName("MarkBroadcastAsRead")]
        [EndpointSummary("Отметить рассылку как прочитанную")]
        [EndpointDescription("Устанавливает статус «Прочитано» для текущего пользователя в конкретной рассылке.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostMarkAsReadAsync(
            [FromRoute, Description("Уникальный идентификатор рассылки (GUID)")] Guid id,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var result = await _broadcastService.MarkAsReadAsync(id, userId);

                return Ok(new
                {
                    IsSuccess = true,
                    Data = result
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при отметке прочтения рассылки {BroadcastId}", id);
                return Unauthorized(new
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении POST /broadcasts/{BroadcastId}/read", id);
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    IsSuccess = false,
                    Error = "Ошибка при отметке прочтения"
                });
            }
        }

        /// <summary>
        /// Получить список моих рассылок.
        /// </summary>
        [HttpGet("my")]
        [EndpointName("GetMyBroadcasts")]
        [EndpointSummary("Получить список моих рассылок")]
        [EndpointDescription("Возвращает список рассылок, адресованных текущему пользователю.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetMyBroadcastsAsync(
            [FromQuery, Description("Фильтр: только непрочитанные")] bool unreadOnly = true,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var broadcasts = await _broadcastService.GetMyBroadcastsAsync(userId, unreadOnly);

                return Ok(new
                {
                    IsSuccess = true,
                    Data = broadcasts
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении списка рассылок");
                return Unauthorized(new
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /broadcasts/my");
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    IsSuccess = false,
                    Error = "Ошибка при получении ваших рассылок"
                });
            }
        }
    }
}