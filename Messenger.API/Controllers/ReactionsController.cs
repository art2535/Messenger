using System.ComponentModel;
using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.Core.DTOs.Reactions;
using Messenger.API.Hubs;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Messenger.Core.Responses;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для управления реакциями на сообщения.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/reactions")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [Tags("Reactions")]
    public class ReactionsController : ControllerBase
    {
        private readonly IReactionService _reactionService;
        private readonly IUserService _userService;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ILogger<ReactionsController> _logger;

        public ReactionsController(
            IReactionService reactionService,
            IUserService userService,
            IHubContext<ChatHub> hubContext,
            ILogger<ReactionsController> logger)
        {
            _reactionService = reactionService;
            _userService = userService;
            _hubContext = hubContext;
            _logger = logger;
        }

        /// <summary>
        /// Получает объект текущего авторизованного пользователя или выбрасывает исключение.
        /// </summary>
        /// <summary>
        /// Получить все реакции на сообщение.
        /// </summary>
        [HttpGet("{messageId:guid}")]
        [EndpointName("GetReactionsByMessage")]
        [EndpointSummary("Получить все реакции на сообщение")]
        [EndpointDescription("Возвращает список всех реакций (эмодзи) на указанное сообщение, включая информацию о пользователе и тип реакции.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetReactionsByMessageAsync(
            [Description("Идентификатор сообщения (GUID)")] Guid messageId,
            CancellationToken ct = default)
        {
            try
            {
                var reactions = await _reactionService.GetReactionsByMessageIdAsync(messageId, ct);

                var data = reactions.Select(r => new
                {
                    ReactionId = r.ReactionId,
                    MessageId = r.MessageId,
                    UserId = r.UserId,
                    ReactionType = r.ReactionType,
                    UserName = r.User != null
                        ? $"{r.User.FirstName} {r.User.LastName}".Trim()
                        : null
                });

                return Ok(ApiResponse.Success(data));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении реакций для сообщения {MessageId}", messageId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Добавить (или обновить) реакцию на сообщение.
        /// </summary>
        [HttpPost("{messageId:guid}")]
        [EndpointName("AddReaction")]
        [EndpointSummary("Добавить реакцию на сообщение")]
        [EndpointDescription("Добавляет реакцию (эмодзи) от имени текущего авторизованного пользователя к указанному сообщению. Если реакция уже есть — обновляет её тип.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostAddReactionAsync(
            [Description("Идентификатор сообщения (GUID)")] Guid messageId,
            [FromBody, Description("Данные реакции")] CreateReactionRequest request,
            CancellationToken ct = default)
        {
            try
            {
                var user = await User.GetCurrentUserAsync(_userService);

                if (request == null || string.IsNullOrWhiteSpace(request.ReactionType))
                {
                    return BadRequest(ApiResponse.Failure("Укажите тип реакции (ReactionType)", "INVALID_REQUEST"));
                }

                var reaction = new Reaction
                {
                    ReactionId = Guid.NewGuid(),
                    MessageId = messageId,
                    UserId = user.UserId,
                    ReactionType = request.ReactionType.Trim()
                };

                await _reactionService.AddReactionAsync(reaction, ct);

                var chatId = await _reactionService.GetChatIdByMessageIdAsync(messageId, ct);
                if (chatId.HasValue)
                {
                    await _hubContext.Clients.Group(chatId.Value.ToString())
                        .SendAsync("ReactionUpdated", new
                        {
                            MessageId = messageId,
                            ChatId = chatId.Value,
                            UserId = user.UserId,
                            UserName = $"{user.FirstName} {user.LastName}".Trim(),
                            ReactionType = reaction.ReactionType,
                            Action = "add"
                        }, ct);
                }

                return Ok(ApiResponse.Success(message: "Реакция добавлена"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при добавлении реакции");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (KeyNotFoundException)
            {
                return NotFound(ApiResponse.Failure("Сообщение не найдено", "NOT_FOUND"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при добавлении реакции к сообщению {MessageId}", messageId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Удалить свою реакцию с сообщения.
        /// </summary>
        [HttpDelete("{messageId:guid}")]
        [EndpointName("DeleteReaction")]
        [EndpointSummary("Удалить свою реакцию с сообщения")]
        [EndpointDescription("Удаляет реакцию текущего авторизованного пользователя с указанного сообщения.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteReactionAsync(
            [Description("Идентификатор сообщения (GUID)")] Guid messageId,
            CancellationToken ct = default)
        {
            try
            {
                var user = await User.GetCurrentUserAsync(_userService);

                if (!await _reactionService.MessageExistsAsync(messageId, ct))
                {
                    return NotFound(ApiResponse.Failure("Сообщение не найдено", "NOT_FOUND"));
                }

                await _reactionService.DeleteReactionAsync(messageId, user.UserId, ct);

                var chatId = await _reactionService.GetChatIdByMessageIdAsync(messageId, ct);
                if (chatId.HasValue)
                {
                    await _hubContext.Clients.Group(chatId.Value.ToString())
                        .SendAsync("ReactionUpdated", new
                        {
                            MessageId = messageId,
                            ChatId = chatId.Value,
                            UserId = user.UserId,
                            UserName = $"{user.FirstName} {user.LastName}".Trim(),
                            ReactionType = (string?)null,
                            Action = "remove"
                        }, ct);
                }

                return Ok(ApiResponse.Success(message: "Реакция успешно удалена"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при удалении реакции");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (KeyNotFoundException)
            {
                return NotFound(ApiResponse.Failure("Сообщение или реакция не найдены", "NOT_FOUND"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении реакции с сообщения {MessageId}", messageId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }
    }
}