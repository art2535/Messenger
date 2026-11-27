using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.Application.DTOs.ChatFolders;
using Messenger.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;

using Messenger.Application.DTOs.Responses;
namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для управления папками чатов.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/chat-folders")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [Tags("ChatFolders")]
    public class ChatFoldersController : ControllerBase
    {
        private readonly IChatFolderService _chatFolderService;
        private readonly IUserService _userService;
        private readonly ILogger<ChatFoldersController> _logger;

        public ChatFoldersController(
            IChatFolderService chatFolderService,
            IUserService userService,
            ILogger<ChatFoldersController> logger)
        {
            _chatFolderService = chatFolderService;
            _userService = userService;
            _logger = logger;
        }

        /// <summary>
        /// Получить все папки чатов текущего пользователя.
        /// </summary>
        [HttpGet]
        [EndpointName("GetChatFolders")]
        [EndpointSummary("Получить все папки чатов")]
        [EndpointDescription("Возвращает список всех папок чатов авторизованного пользователя.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAllAsync(CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var list = await _chatFolderService.GetFoldersAsync(userId, ct);

                return Ok(ApiResponse.Success(list));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении папок чатов");
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /chat-folders");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Получить папку чатов по ID.
        /// </summary>
        [HttpGet("{folderId:guid}")]
        [EndpointName("GetChatFolderById")]
        [EndpointSummary("Получить папку чатов")]
        [EndpointDescription("Возвращает информацию о папке чатов по её идентификатору.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetByIdAsync(
            [FromRoute, Description("Уникальный идентификатор папки (GUID)")] Guid folderId,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var folder = await _chatFolderService.GetFolderAsync(userId, folderId, ct);

                if (folder == null)
                {
                    return NotFound(ApiResponse.Failure("Папка не найдена", "NOT_FOUND"));
                }

                return Ok(ApiResponse.Success(folder));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при получении папки {FolderId}", folderId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении GET /chat-folders/{FolderId}", folderId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Создать папку чатов.
        /// </summary>
        [HttpPost]
        [EndpointName("CreateChatFolder")]
        [EndpointSummary("Создать папку чатов")]
        [EndpointDescription("Создаёт новую папку чатов для текущего пользователя.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostCreateAsync(
            [FromBody, Description("Данные для создания папки чатов")] CreateChatFolderRequest request,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var folder = await _chatFolderService.CreateFolderAsync(userId, request, ct);

                return Ok(ApiResponse.Success(folder));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при создании папки чатов");
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
                _logger.LogError(ex, "Ошибка при выполнении POST /chat-folders");
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Обновить папку чатов.
        /// </summary>
        [HttpPut("{folderId:guid}")]
        [EndpointName("UpdateChatFolder")]
        [EndpointSummary("Обновить папку чатов")]
        [EndpointDescription("Обновляет параметры существующей папки чатов.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PutUpdateAsync(
            [FromRoute, Description("Уникальный идентификатор папки (GUID)")] Guid folderId,
            [FromBody, Description("Новые данные папки чатов")] UpdateChatFolderRequest request,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var folder = await _chatFolderService.UpdateFolderAsync(userId, folderId, request, ct);

                if (folder == null)
                {
                    return NotFound(ApiResponse.Failure("Папка не найдена", "NOT_FOUND"));
                }

                return Ok(ApiResponse.Success(folder));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при обновлении папки {FolderId}", folderId);
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
                _logger.LogError(ex, "Ошибка при выполнении PUT /chat-folders/{FolderId}", folderId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Удалить папку чатов.
        /// </summary>
        [HttpDelete("{folderId:guid}")]
        [EndpointName("DeleteChatFolder")]
        [EndpointSummary("Удалить папку чатов")]
        [EndpointDescription("Удаляет папку чатов по её идентификатору.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteAsync(
            [FromRoute, Description("Уникальный идентификатор папки (GUID)")] Guid folderId,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var ok = await _chatFolderService.DeleteFolderAsync(userId, folderId, ct);

                if (!ok)
                {
                    return NotFound(ApiResponse.Failure("Папка не найдена", "NOT_FOUND"));
                }

                return Ok(ApiResponse.Success());
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при удалении папки {FolderId}", folderId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении DELETE /chat-folders/{FolderId}", folderId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Добавить чат в папку.
        /// </summary>
        [HttpPost("{folderId:guid}/chats")]
        [EndpointName("AddChatToFolder")]
        [EndpointSummary("Добавить чат в папку")]
        [EndpointDescription("Добавляет указанный чат в выбранную папку чатов.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostAddChatAsync(
            [FromRoute, Description("Уникальный идентификатор папки (GUID)")] Guid folderId,
            [FromBody, Description("Данные чата для добавления в папку")] FolderChatRequest request,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var ok = await _chatFolderService.AddChatAsync(userId, folderId, request.ChatId, ct);

                if (!ok)
                {
                    return NotFound(ApiResponse.Failure("Папка или чат не найдены", "NOT_FOUND"));
                }

                return Ok(ApiResponse.Success());
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при добавлении чата в папку {FolderId}", folderId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении POST /chat-folders/{FolderId}/chats", folderId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Удалить чат из папки.
        /// </summary>
        [HttpDelete("{folderId:guid}/chats/{chatId:guid}")]
        [EndpointName("RemoveChatFromFolder")]
        [EndpointSummary("Удалить чат из папки")]
        [EndpointDescription("Удаляет указанный чат из папки чатов.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteRemoveChatAsync(
            [FromRoute, Description("Уникальный идентификатор папки (GUID)")] Guid folderId,
            [FromRoute, Description("Уникальный идентификатор чата (GUID)")] Guid chatId,
            CancellationToken ct = default)
        {
            try
            {
                var userId = await User.GetCurrentUserIdAsync(_userService);
                var ok = await _chatFolderService.RemoveChatAsync(userId, folderId, chatId, ct);

                if (!ok)
                {
                    return NotFound(ApiResponse.Failure("Папка не найдена", "NOT_FOUND"));
                }

                return Ok(ApiResponse.Success());
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ошибка авторизации при удалении чата {ChatId} из папки {FolderId}", chatId, folderId);
                return Unauthorized(ApiResponse.Failure(ex.Message, "UNAUTHORIZED"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении DELETE /chat-folders/{FolderId}/chats/{ChatId}", folderId, chatId);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }
    }
}