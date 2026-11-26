using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.Core.DTOs.Chats;
using Messenger.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;
using Messenger.Core.Responses;
using Messenger.Infrastructure.Interfaces.Chats;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для управления чатами.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/chats")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [Tags("Chats")]
    public class ChatsController : ControllerBase
    {
        private readonly IChatQueryWorkflowService _chatQueryWorkflowService;
        private readonly IChatManagementWorkflowService _chatManagementWorkflowService;
        private readonly IUserService _userService;
        private readonly ILogger<ChatsController> _logger;

        public ChatsController(
            IChatQueryWorkflowService chatQueryWorkflowService,
            IChatManagementWorkflowService chatManagementWorkflowService,
            IUserService userService,
            ILogger<ChatsController> logger)
        {
            _chatQueryWorkflowService = chatQueryWorkflowService;
            _chatManagementWorkflowService = chatManagementWorkflowService;
            _userService = userService;
            _logger = logger;
        }

        /// <summary>
        /// Получить список чатов текущего пользователя.
        /// </summary>
        [HttpGet]
        [EndpointName("GetUserChats")]
        [EndpointSummary("Получить список чатов текущего пользователя")]
        [EndpointDescription("Возвращает все чаты, в которых состоит авторизованный пользователь, с информацией о последнем сообщении.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetUserChatsAsync(CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var chats = await _chatQueryWorkflowService.GetUserChatsAsync(userId, ct);

                return Ok(ApiResponse.Success(chats));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении списка чатов");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /chats");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Получить информацию о конкретном чате.
        /// </summary>
        [HttpGet("{chatId:guid}")]
        [EndpointName("GetChatById")]
        [EndpointSummary("Получить информацию о конкретном чате")]
        [EndpointDescription("Возвращает детали чата: название, тип, участников и аватар (для приватных чатов — аватар собеседника).")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetChatByIdAsync(
            [FromRoute, Description("Уникальный идентификатор чата (GUID)")] Guid chatId,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);

                var result = await _chatQueryWorkflowService.GetChatDetailsAsync(chatId, userId, ct);

                if (result.Status == ChatQueryStatus.NotFound)
                {
                    return NotFound(ApiResponse.Failure(result.Error!, "NOT_FOUND"));
                }

                if (result.Status == ChatQueryStatus.Forbidden)
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        ApiResponse.Failure(result.Error!, "FORBIDDEN"));
                }

                return Ok(ApiResponse.Success(result.Data));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении чата {ChatId}", chatId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /chats/{ChatId}", chatId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Создать новый чат.
        /// </summary>
        [HttpPost("create-chat")]
        [EndpointName("CreateChat")]
        [EndpointSummary("Создать новый чат")]
        [EndpointDescription("Создаёт приватный или групповой чат. Для приватного — ровно один участник, для группового — название обязательно.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostCreateChatAsync(
            [FromBody, Description("Данные для создания чата")] CreateChatRequest request,
            CancellationToken ct = default)
        {
            if (!new[] { "private", "group" }.Contains(request.Type))
            {
                return BadRequest(ApiResponse.Failure("Тип чата должен быть 'private' или 'group'", "INVALID_REQUEST"));
            }

            if (request.Type == "group" && string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(ApiResponse.Failure("Для группового чата укажите название", "INVALID_REQUEST"));
            }

            if (request.UserIds == null || request.UserIds.Count == 0)
            {
                return BadRequest(ApiResponse.Failure("Выберите хотя бы одного участника", "INVALID_REQUEST"));
            }

            if (request.Type == "private" && request.UserIds.Count != 1)
            {
                return BadRequest(ApiResponse.Failure("В приватном чате должен быть ровно один участник", "INVALID_REQUEST"));
            }

            try
            {
                var currentUserId = await User.GetCurrentUserIdAsync(_userService);
                var result = await _chatManagementWorkflowService.CreateChatAsync(
                    request,
                    currentUserId,
                    ct);

                return Ok(ApiResponse.Success(new
                {
                    result.ChatId,
                    result.Name
                }));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при создании чата");
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
                _logger.LogError(ex, "Ошибка при выполнении POST /chats/create-chat");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Добавить участника в чат.
        /// </summary>
        [HttpPost("{chatId:guid}/{userId:guid}/participant")]
        [EndpointName("AddParticipantToChat")]
        [EndpointSummary("Добавить участника в чат")]
        [EndpointDescription("Добавляет пользователя в существующий чат и уведомляет всех участников через SignalR.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostAddParticipantToChatAsync(
            [FromRoute, Description("Идентификатор чата")] Guid chatId,
            [FromRoute, Description("Идентификатор добавляемого пользователя")] Guid userId,
            [FromQuery, Description("Роль в чате (по умолчанию «участник»)")] string role = "участник",
            CancellationToken ct = default)
        {
            try
            {
                await User.GetCurrentUserIdAsync(_userService);
                var result = await _chatManagementWorkflowService.AddParticipantAsync(
                    chatId, userId, role, ct);

                if (result.Status == ChatManagementStatus.NotFound)
                {
                    return NotFound(ApiResponse.Failure(result.Message!, "NOT_FOUND"));
                }

                return Ok(ApiResponse.Success(message: result.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при добавлении участника {UserId} в чат {ChatId}", userId, chatId);
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
                _logger.LogError(ex, "Ошибка при выполнении POST /chats/{ChatId}/{UserId}/participant", chatId, userId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Обновить название чата.
        /// </summary>
        [HttpPut("{chatId:guid}")]
        [EndpointName("UpdateChat")]
        [EndpointSummary("Обновить название чата")]
        [EndpointDescription("Изменяет название группового чата.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PutUpdateChatAsync(
            [FromRoute, Description("Идентификатор чата")] Guid chatId,
            [FromBody, Description("Новые данные чата")] UpdateChatRequest request,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(ApiResponse.Failure("Название не может быть пустым", "INVALID_REQUEST"));
            }

            try
            {
                await User.GetCurrentUserIdAsync(_userService);
                var result = await _chatManagementWorkflowService.UpdateChatAsync(chatId, request.Name, ct);
                if (result.Status == ChatManagementStatus.NotFound)
                {
                    return NotFound(ApiResponse.Failure(result.Message!, "NOT_FOUND"));
                }

                return Ok(ApiResponse.Success(message: result.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при обновлении чата {ChatId}", chatId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении PUT /chats/{ChatId}", chatId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Удалить чат.
        /// </summary>
        [HttpDelete("{chatId:guid}")]
        [EndpointName("DeleteChat")]
        [EndpointSummary("Удалить чат")]
        [EndpointDescription("Полностью удаляет чат и все связанные данные.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteChatAsync(
            [FromRoute, Description("Идентификатор удаляемого чата")] Guid chatId,
            CancellationToken ct = default)
        {
            try
            {
                await User.GetCurrentUserIdAsync(_userService);
                var result = await _chatManagementWorkflowService.DeleteChatAsync(chatId, ct);
                if (result.Status == ChatManagementStatus.NotFound)
                {
                    return NotFound(ApiResponse.Failure(result.Message!, "NOT_FOUND"));
                }

                return Ok(ApiResponse.Success(message: result.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при удалении чата {ChatId}", chatId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении DELETE /chats/{ChatId}", chatId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Удалить участника из чата.
        /// </summary>
        [HttpDelete("{chatId:guid}/{userId:guid}")]
        [EndpointName("RemoveParticipantFromChat")]
        [EndpointSummary("Удалить участника из чата")]
        [EndpointDescription("Исключает пользователя из чата и отправляет соответствующие SignalR-уведомления.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteParticipantFromChatAsync(
            [FromRoute, Description("Идентификатор чата")] Guid chatId,
            [FromRoute, Description("Идентификатор удаляемого участника")] Guid userId,
            CancellationToken ct = default)
        {
            try
            {
                var currentUserId = await User.GetCurrentUserIdAsync(_userService);
                var result = await _chatManagementWorkflowService.RemoveParticipantAsync(
                    chatId, userId, currentUserId, ct);

                if (result.Status == ChatManagementStatus.NotFound)
                {
                    return NotFound(ApiResponse.Failure(result.Message!, "NOT_FOUND"));
                }

                if (result.Status == ChatManagementStatus.Forbidden)
                {
                    if (result.UseLegacyErrorResponse)
                    {
                        return StatusCode(StatusCodes.Status403Forbidden, new
                        {
                            IsSuccess = false,
                            Error = result.Message
                        });
                    }

                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        ApiResponse.Failure(result.Message!, "FORBIDDEN"));
                }

                if (result.Status == ChatManagementStatus.BadRequest)
                {
                    return BadRequest(ApiResponse.Failure(result.Message!, "INVALID_REQUEST"));
                }

                return Ok(ApiResponse.Success(message: result.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при удалении участника {UserId} из чата {ChatId}", userId, chatId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении DELETE /chats/{ChatId}/{UserId}", chatId, userId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

    }
}