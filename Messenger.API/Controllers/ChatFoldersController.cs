using Asp.Versioning;
using Messenger.API.Responses;
using Messenger.API.Services;
using Messenger.Core.DTOs.ChatFolders;
using Messenger.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/chat-folders")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Tags("ChatFolders")]
    public class ChatFoldersController : ControllerBase
    {
        private readonly IChatFolderService _folders;
        private readonly IUserService _users;

        public ChatFoldersController(IChatFolderService folders, IUserService users)
        {
            _folders = folders;
            _users = users;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(CancellationToken ct = default)
        {
            var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _users);
            if (error != null) return error;
            var list = await _folders.GetFoldersAsync(user!.UserId, ct);
            return Ok(new { isSuccess = true, data = list });
        }

        [HttpGet("{folderId:guid}")]
        public async Task<IActionResult> GetOne(Guid folderId, CancellationToken ct = default)
        {
            var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _users);
            if (error != null) return error;
            var folder = await _folders.GetFolderAsync(user!.UserId, folderId, ct);
            if (folder == null)
                return NotFound(new ErrorResponse { IsSuccess = false, Error = "Папка не найдена" });
            return Ok(new { isSuccess = true, data = folder });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateChatFolderRequest request, CancellationToken ct = default)
        {
            var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _users);
            if (error != null) return error;
            try
            {
                var folder = await _folders.CreateFolderAsync(user!.UserId, request, ct);
                return Ok(new { isSuccess = true, data = folder });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ErrorResponse { IsSuccess = false, Error = ex.Message });
            }
        }

        [HttpPut("{folderId:guid}")]
        public async Task<IActionResult> Update(Guid folderId, [FromBody] UpdateChatFolderRequest request, CancellationToken ct = default)
        {
            var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _users);
            if (error != null) return error;
            try
            {
                var folder = await _folders.UpdateFolderAsync(user!.UserId, folderId, request, ct);
                if (folder == null)
                    return NotFound(new ErrorResponse { IsSuccess = false, Error = "Папка не найдена" });
                return Ok(new { isSuccess = true, data = folder });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ErrorResponse { IsSuccess = false, Error = ex.Message });
            }
        }

        [HttpDelete("{folderId:guid}")]
        public async Task<IActionResult> Delete(Guid folderId, CancellationToken ct = default)
        {
            var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _users);
            if (error != null) return error;
            var ok = await _folders.DeleteFolderAsync(user!.UserId, folderId, ct);
            if (!ok)
                return NotFound(new ErrorResponse { IsSuccess = false, Error = "Папка не найдена" });
            return Ok(new { isSuccess = true });
        }

        [HttpPost("{folderId:guid}/chats")]
        public async Task<IActionResult> AddChat(Guid folderId, [FromBody] FolderChatRequest request, CancellationToken ct = default)
        {
            var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _users);
            if (error != null) return error;
            var ok = await _folders.AddChatAsync(user!.UserId, folderId, request.ChatId, ct);
            if (!ok)
                return NotFound(new ErrorResponse { IsSuccess = false, Error = "Папка или чат не найдены" });
            return Ok(new { isSuccess = true });
        }

        [HttpDelete("{folderId:guid}/chats/{chatId:guid}")]
        public async Task<IActionResult> RemoveChat(Guid folderId, Guid chatId, CancellationToken ct = default)
        {
            var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _users);
            if (error != null) return error;
            var ok = await _folders.RemoveChatAsync(user!.UserId, folderId, chatId, ct);
            if (!ok)
                return NotFound(new ErrorResponse { IsSuccess = false, Error = "Папка не найдена" });
            return Ok(new { isSuccess = true });
        }
    }
}
