using Asp.Versioning;
using Messenger.API.Responses;
using Messenger.API.Services;
using Messenger.Core.DTOs.Chats;
using Messenger.Core.Hubs;
using Messenger.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.ComponentModel;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для управления чатами
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [Tags("Chats")]
    public class ChatsController : ControllerBase
    {
        private readonly IChatService _chatService;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IUserService _userService;

        public ChatsController(IChatService chatService, IHubContext<ChatHub> hubContext, IUserService userService)
        {
            _chatService = chatService;
            _hubContext = hubContext;
            _userService = userService;
        }

        /// <summary>
        /// Получить список чатов текущего пользователя
        /// </summary>
        [HttpGet]
        [EndpointName("GetUserChats")]
        [EndpointSummary("Получить список чатов текущего пользователя")]
        [EndpointDescription("Возвращает все чаты, в которых состоит авторизованный пользователь, с информацией о последнем сообщении.")]
        [ProducesResponseType(typeof(GetUserChatsSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetChatsByIdAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                {
                    return error;
                }

                var chats = await _chatService.GetUserChatsWithLastMessageAsync(user!.UserId, cancellationToken);

                return Ok(new GetUserChatsSuccessResponse
                {
                    IsSuccess = true,
                    Data = chats
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
        /// Получить информацию о конкретном чате
        /// </summary>
        [HttpGet("{chatId}")]
        [EndpointName("GetChatById")]
        [EndpointSummary("Получить информацию о конкретном чате")]
        [EndpointDescription("Возвращает детали чата: название, тип, участников и аватар (для приватных чатов — аватар собеседника).")]
        [ProducesResponseType(typeof(GetChatByIdSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetChatByIdAsync(
            [Description("Уникальный идентификатор чата (GUID)")] Guid chatId, 
            CancellationToken ct = default)
        {
            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                {
                    return error;
                }

                var chat = await _chatService.GetChatByIdAsync(chatId, ct);
                if (chat == null)
                {
                    return NotFound(new ErrorResponse
                    { 
                        IsSuccess = false, 
                        Error = "Чат не найден" 
                    });
                }

                var participants = await _chatService.GetChatParticipantsAsync(chatId, ct);
                if (!participants.Any(p => p.UserId == user!.UserId))
                    return Forbid();

                var participantDtos = participants.Select(p =>
                {
                    var role = p.Role;
                    if (string.IsNullOrWhiteSpace(role))
                        role = p.UserId == chat.UserId ? "владелец" : "участник";
                    else if (p.UserId == chat.UserId && role.Trim().ToLowerInvariant() is not ("владелец" or "owner"))
                        role = "владелец";

                    return new
                    {
                        id = p.UserId,
                        userId = p.UserId,
                        name = $"{p.User?.FirstName} {p.User?.LastName}".Trim(),
                        fullName = $"{p.User?.FirstName} {p.User?.LastName}".Trim(),
                        avatar = p.User?.Account?.Avatar,
                        role
                    };
                }).ToList();

                string displayName = chat.Type == "group"
                    ? chat.Name
                    : await GetPrivateChatDisplayNameAsync(chatId, user!.UserId, ct);

                var result = new
                {
                    chatId = chat.ChatId,
                    name = displayName,
                    type = chat.Type,
                    userId = chat.UserId,
                    creatorId = chat.UserId,
                    avatar = chat.Type == "group" ? (string?)null : 
                        await GetOtherUserAvatarAsync(participantDtos.FirstOrDefault(p => p.id != user!.UserId)?.id 
                        ?? user!.UserId, ct),
                    participants = participantDtos
                };

                return Ok(new GetChatByIdSuccessResponse 
                { 
                    IsSuccess = true, 
                    Data = result 
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
        /// Создать новый чат
        /// </summary>
        [HttpPost("create-chat")]
        [EndpointName("CreateChat")]
        [EndpointSummary("Создать новый чат")]
        [EndpointDescription("Создаёт приватный или групповой чат. Для приватного — ровно один участник, для группового — название обязательно.")]
        [ProducesResponseType(typeof(CreateChatSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateNewChatAsync(
            [FromBody, Description("Данные для создания чата")] CreateChatRequest request, 
            CancellationToken ct = default)
        {
            if (!new[] { "private", "group" }.Contains(request.Type))
            {
                return BadRequest(new ErrorResponse
                {
                    IsSuccess = false,
                    Error = "Тип чата должен быть 'private' или 'group'"
                });
            }

            if (request.Type == "group" && string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new ErrorResponse
                {
                    IsSuccess = false,
                    Error = "Для группового чата укажите название"
                });
            }

            if (request.UserIds == null || request.UserIds.Count == 0)
            {
                return BadRequest(new ErrorResponse
                {
                    IsSuccess = false,
                    Error = "Выберите хотя бы одного участника"
                });
            }

            if (request.Type == "private" && request.UserIds.Count != 1)
            {
                return BadRequest(new ErrorResponse
                {
                    IsSuccess = false,
                    Error = "В приватном чате должен быть ровно один участник"
                });
            }

            try
            {
                var (user, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                {
                    return error;
                }

                string chatNameForDb = request.Type == "group"
                    ? request.Name.Trim()
                    : "Приватный чат";

                var chat = await _chatService.CreateChatAsync(
                    name: chatNameForDb,
                    type: request.Type,
                    creatorId: user!.UserId,
                    ct);

                foreach (var userId in request.UserIds.Distinct())
                {
                    if (userId == user!.UserId) 
                        continue;

                    await _chatService.AddParticipantToChatAsync(chat.ChatId, userId, "участник", ct);
                }

                string displayName = request.Type == "group"
                    ? request.Name.Trim()
                    : await GetPrivateChatDisplayNameAsync(chat.ChatId, user!.UserId, ct);

                foreach (var userId in request.UserIds.Distinct())
                {
                    var dbParticipant = await _userService.GetUserByIdAsync(userId, ct);
                    if (dbParticipant == null || string.IsNullOrEmpty(dbParticipant.ExternalId)) continue;

                    string individualizedName = request.Type == "group"
                        ? request.Name.Trim()
                        : await GetPrivateChatDisplayNameAsync(chat.ChatId, userId, ct);

                    string? individualizedAvatar = null;
                    if (request.Type == "private")
                    {
                        var otherId = (userId == user!.UserId) ? request.UserIds[0] : user!.UserId;
                        individualizedAvatar = await GetOtherUserAvatarAsync(otherId, ct);
                    }

                    var chatForSingleUser = new
                    {
                        chatId = chat.ChatId,
                        name = individualizedName,
                        avatar = individualizedAvatar,
                        type = chat.Type
                    };

                    await _hubContext.Clients.User(dbParticipant.ExternalId.ToLowerInvariant())
                        .SendAsync("NewChat", chatForSingleUser, ct);
                }

                return Ok(new CreateChatSuccessResponse 
                { 
                    IsSuccess = true, 
                    Data = new 
                    { 
                        chat.ChatId, 
                        Name = displayName 
                    } 
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Добавить участника в чат
        /// </summary>
        [HttpPost("{chatId}/{userId}/participant")]
        [EndpointName("AddParticipantToChat")]
        [EndpointSummary("Добавить участника в чат")]
        [EndpointDescription("Добавляет пользователя в существующий чат и уведомляет всех участников через SignalR.")]
        [ProducesResponseType(typeof(AddParticipantSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddParticipantToChatAsync(
            [Description("Идентификатор чата")] Guid chatId,
            [Description("Идентификатор добавляемого пользователя")] Guid userId,
            [Description("Роль в чате (по умолчанию «участник»)")] string role = "участник",
            CancellationToken ct = default)
        {
            try
            {
                var (currentUser, error) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (error != null)
                {
                    return error;
                }

                var chat = await _chatService.GetChatByIdAsync(chatId, ct);
                if (chat == null)
                {
                    return NotFound();
                }

                await _chatService.AddParticipantToChatAsync(chatId, userId, role, ct);

                var addedUser = await _userService.GetUserByIdAsync(userId, ct);
                if (addedUser == null)
                {
                    return NotFound(new ErrorResponse 
                    {
                        IsSuccess = false,
                        Error = "Пользователь не найден"
                    });
                }

                var userInfo = new
                {
                    id = addedUser.UserId,
                    name = $"{addedUser.FirstName} {addedUser.LastName}".Trim(),
                    fullName = $"{addedUser.FirstName} {addedUser.LastName}".Trim(),
                    avatar = addedUser.Account?.Avatar
                };

                await _hubContext.Clients.Group(chatId.ToString()).
                    SendAsync("ParticipantAdded", new { chatId, user = userInfo }, ct);

                var updatedChat = await _chatService.GetChatByIdAsync(chatId, ct);
                var updatedCount = updatedChat?.ChatParticipants?.Count ?? 0;

                await _hubContext.Clients.Group(chatId.ToString())
                    .SendAsync("ParticipantCountChanged", new
                    {
                        chatId = chatId,
                        count = updatedCount
                    });

                var chatForNewUser = new
                {
                    chatId = chat.ChatId,
                    name = chat.Type == "group" ? chat.Name : await GetPrivateChatDisplayNameAsync(chatId, userId, ct),
                    type = chat.Type
                };

                if (addedUser?.ExternalId != null)
                {
                    await _hubContext.Clients.User(addedUser.ExternalId.ToLowerInvariant())
                        .SendAsync("NewChat", chatForNewUser, ct);
                }

                return Ok(new AddParticipantSuccessResponse
                {
                    IsSuccess = true,
                    Message = $"Пользователь {userId} успешно добавлен в чат"
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
        /// Обновить название чата
        /// </summary>
        [HttpPut("{chatId}")]
        [EndpointName("UpdateChat")]
        [EndpointSummary("Обновить название чата")]
        [EndpointDescription("Изменяет название группового чата.")]
        [ProducesResponseType(typeof(UpdateChatSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateChatAsync(
            [Description("Идентификатор чата")] Guid chatId,
            [FromBody, Description("Новые данные чата")] UpdateChatRequest request,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new ErrorResponse
                {
                    IsSuccess = false,
                    Error = "Название не может быть пустым"
                });
            }

            try
            {
                var chat = await _chatService.GetChatByIdAsync(chatId, ct);
                if (chat == null) 
                {
                    return NotFound(new ErrorResponse
                    {
                        IsSuccess = false,
                        Error = "Чат не найден"
                    });
                }

                chat.Name = request.Name;

                await _chatService.UpdateChatAsync(chat, ct);

                await _hubContext.Clients.All.SendAsync("ChatUpdated", new
                {
                    chatId,
                    name = request.Name
                }, ct);

                return Ok(new UpdateChatSuccessResponse
                { 
                    IsSuccess = true, 
                    Message = "Чат обновлён" 
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
        /// Удалить чат
        /// </summary>
        [HttpDelete("{chatId}")]
        [EndpointName("DeleteChat")]
        [EndpointSummary("Удалить чат")]
        [EndpointDescription("Полностью удаляет чат и все связанные данные.")]
        [ProducesResponseType(typeof(DeleteChatSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteChatAsync(
            [Description("Идентификатор удаляемого чата")] Guid chatId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var chat = await _chatService.GetChatByIdAsync(chatId, cancellationToken);
                if (chat == null)
                {
                    return NotFound(new ErrorResponse
                    {
                        IsSuccess = false,
                        Error = "Чат не найден"
                    });
                }

                await _chatService.DeleteChatAsync(chat, cancellationToken);

                await _hubContext.Clients.All.SendAsync("ChatDeleted", chatId, cancellationToken);

                return Ok(new DeleteChatSuccessResponse
                {
                    IsSuccess = true,
                    Message = "Чат успешно удален"
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
        /// Удалить участника из чата
        /// </summary>
        [HttpDelete("{chatId}/{userId}")]
        [EndpointName("RemoveParticipantFromChat")]
        [EndpointSummary("Удалить участника из чата")]
        [EndpointDescription("Исключает пользователя из чата и отправляет соответствующие SignalR-уведомления.")]
        [ProducesResponseType(typeof(RemoveParticipantSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteParticipantsFromChatAsync(
            [Description("Идентификатор чата")] Guid chatId,
            [Description("Идентификатор удаляемого участника")] Guid userId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (currentUser, authError) = await UserValidationService.GetCurrentUserOrErrorAsync(User, _userService);
                if (authError != null)
                    return authError;

                var userToDelete = await _userService.GetUserByIdAsync(userId, cancellationToken);

                if (userToDelete == null)
                {
                    return NotFound(new ErrorResponse { IsSuccess = false, Error = "Пользователь не найден" });
                }

                var participants = (await _chatService.GetChatParticipantsAsync(chatId, cancellationToken)).ToList();
                var actor = participants.FirstOrDefault(p => p.UserId == currentUser!.UserId);
                if (actor == null)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                    {
                        IsSuccess = false,
                        Error = "Вы не являетесь участником этого чата"
                    });
                }

                var target = participants.FirstOrDefault(p => p.UserId == userId);
                if (target == null)
                {
                    return NotFound(new ErrorResponse { IsSuccess = false, Error = "Участник не найден в чате" });
                }

                static string NormRole(string? role) =>
                    string.IsNullOrWhiteSpace(role) ? "участник" : role.Trim().ToLowerInvariant();

                static bool IsAdminOrOwnerRole(string? role)
                {
                    var r = NormRole(role);
                    return r is "владелец" or "owner" or "admin" or "админ" or "administrator" or "модератор" or "moderator";
                }

                var actorRole = NormRole(actor.Role);
                var targetRole = NormRole(target.Role);

                if (userId == currentUser!.UserId)
                {
                    return BadRequest(new ErrorResponse
                    {
                        IsSuccess = false,
                        Error = "Нельзя удалить самого себя из чата этим методом"
                    });
                }

                if (IsAdminOrOwnerRole(actor.Role))
                {
                    if (IsAdminOrOwnerRole(target.Role))
                    {
                        return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                        {
                            IsSuccess = false,
                            Error = "Администратор не может удалить владельца или другого администратора"
                        });
                    }
                }
                else
                {
                    if (IsAdminOrOwnerRole(target.Role))
                    {
                        return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                        {
                            IsSuccess = false,
                            Error = "Нельзя удалить владельца или администратора чата"
                        });
                    }
                }

                await _chatService.DeleteParticipantFromChatAsync(chatId, userId, cancellationToken);

                await _hubContext.Clients.Group(chatId.ToString().ToLowerInvariant())
                    .SendAsync("ParticipantRemoved", new { chatId, userId }, cancellationToken);

                var updatedChat = await _chatService.GetChatByIdAsync(chatId, cancellationToken);
                var updatedCount = updatedChat?.ChatParticipants?.Count ?? 0;

                await _hubContext.Clients.Group(chatId.ToString())
                    .SendAsync("ParticipantCountChanged", new
                    {
                        chatId = chatId,
                        count = updatedCount
                    });

                if (!string.IsNullOrEmpty(userToDelete.ExternalId))
                {
                    var signalRId = userToDelete.ExternalId.ToLowerInvariant();

                    await _hubContext.Clients.User(signalRId)
                        .SendAsync("YouWereRemovedFromChat", chatId, cancellationToken);
                }

                return Ok(new RemoveParticipantSuccessResponse
                {
                    IsSuccess = true,
                    Message = $"Пользователь {userId} успешно удален из чата"
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

        private async Task<string> GetPrivateChatDisplayNameAsync(Guid chatId, Guid currentUserId, CancellationToken ct)
        {
            var participants = await _chatService.GetChatParticipantsAsync(chatId, ct);

            var otherParticipant = participants
                .FirstOrDefault(p => p.UserId != currentUserId);

            if (otherParticipant?.User == null)
                return "Удалённый пользователь";

            return $"{otherParticipant.User.FirstName} {otherParticipant.User.LastName}".Trim();
        }

        private async Task<string?> GetOtherUserAvatarAsync(Guid userId, CancellationToken ct)
        {
            var user = await _userService.GetUserByIdAsync(userId, ct);
            return user?.Account?.Avatar;
        }
    }
}
