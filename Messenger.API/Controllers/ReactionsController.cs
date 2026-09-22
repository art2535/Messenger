using Asp.Versioning;
using Messenger.API.Responses;
using Messenger.API.Services;
using Messenger.Core.DTOs.Reactions;
using Messenger.Core.Hubs;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.ComponentModel;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для управления реакциями на сообщения
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [Tags("Reactions")]
    public class ReactionsController : ControllerBase
    {
        private readonly IReactionService _reactionService;
        private readonly IUserService _userService;
        private readonly IHubContext<ChatHub> _hubContext;

        public ReactionsController(IReactionService reactionService, IUserService userService,
            IHubContext<ChatHub> hubContext)
        {
            _reactionService = reactionService;
            _userService = userService;
            _hubContext = hubContext;
        }

        /// <summary>
        /// Получить все реакции на сообщение
        /// </summary>
        [HttpGet("{messageId:guid}")]
        [EndpointName("GetReactionsByMessage")]
        [EndpointSummary("Получить все реакции на сообщение")]
        [EndpointDescription("Возвращает список всех реакций (эмодзи) на указанное сообщение, включая информацию о пользователе и тип реакции.")]
        [ProducesResponseType(typeof(GetReactionsSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetReactionsByMessageAsync(
            [Description("Идентификатор сообщения (GUID)")] Guid messageId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var reactions = await _reactionService.GetReactionsByMessageIdAsync(messageId, cancellationToken);

                var data = reactions.Select(r => new
                {
                    reactionId = r.ReactionId,
                    messageId = r.MessageId,
                    userId = r.UserId,
                    reactionType = r.ReactionType,
                    userName = r.User != null
                        ? $"{r.User.FirstName} {r.User.LastName}".Trim()
                        : null
                });

                return Ok(new GetReactionsSuccessResponse
                {
                    IsSuccess = true,
                    Data = data
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
        /// Добавить (или обновить) реакцию на сообщение
        /// </summary>
        [HttpPost("{messageId:guid}")]
        [EndpointName("AddReaction")]
        [EndpointSummary("Добавить реакцию на сообщение")]
        [EndpointDescription("Добавляет реакцию (эмодзи) от имени текущего авторизованного пользователя к указанному сообщению. Если реакция уже есть — обновляет её тип.")]
        [ProducesResponseType(typeof(AddReactionSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddReactionAsync(
            [Description("Идентификатор сообщения (GUID)")] Guid messageId,
            [FromBody, Description("Данные реакции")] CreateReactionRequest request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                    return error;

                if (request == null || string.IsNullOrWhiteSpace(request.ReactionType))
                {
                    return BadRequest(new ErrorResponse
                    {
                        IsSuccess = false,
                        Error = "Укажите тип реакции (ReactionType)"
                    });
                }

                var reaction = new Reaction
                {
                    ReactionId = Guid.NewGuid(),
                    MessageId = messageId,
                    UserId = user!.UserId,
                    ReactionType = request.ReactionType.Trim()
                };

                await _reactionService.AddReactionAsync(reaction, cancellationToken);

                var chatId = await _reactionService.GetChatIdByMessageIdAsync(messageId, cancellationToken);
                if (chatId.HasValue)
                {
                    await _hubContext.Clients.Group(chatId.Value.ToString())
                        .SendAsync("ReactionUpdated", new
                        {
                            messageId,
                            chatId = chatId.Value,
                            userId = user.UserId,
                            userName = $"{user.FirstName} {user.LastName}".Trim(),
                            reactionType = reaction.ReactionType,
                            action = "add"
                        }, cancellationToken);
                }

                return Ok(new AddReactionSuccessResponse
                {
                    IsSuccess = true,
                    Message = "Реакция добавлена"
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new ErrorResponse
                {
                    IsSuccess = false,
                    Error = "Сообщение не найдено"
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
        /// Удалить свою реакцию с сообщения
        /// </summary>
        [HttpDelete("{messageId:guid}")]
        [EndpointName("DeleteReaction")]
        [EndpointSummary("Удалить свою реакцию с сообщения")]
        [EndpointDescription("Удаляет реакцию текущего авторизованного пользователя с указанного сообщения.")]
        [ProducesResponseType(typeof(DeleteReactionSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteReactionAsync(
            [Description("Идентификатор сообщения (GUID)")] Guid messageId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                    return error;

                if (!await _reactionService.MessageExistsAsync(messageId, cancellationToken))
                {
                    return NotFound(new ErrorResponse
                    {
                        IsSuccess = false,
                        Error = "Сообщение не найдено"
                    });
                }

                await _reactionService.DeleteReactionAsync(messageId, user!.UserId, cancellationToken);

                var chatId = await _reactionService.GetChatIdByMessageIdAsync(messageId, cancellationToken);
                if (chatId.HasValue)
                {
                    await _hubContext.Clients.Group(chatId.Value.ToString())
                        .SendAsync("ReactionUpdated", new
                        {
                            messageId,
                            chatId = chatId.Value,
                            userId = user.UserId,
                            userName = $"{user.FirstName} {user.LastName}".Trim(),
                            reactionType = (string?)null,
                            action = "remove"
                        }, cancellationToken);
                }

                return Ok(new DeleteReactionSuccessResponse
                {
                    IsSuccess = true,
                    Message = "Реакция успешно удалена"
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
