using Messenger.Application.DTOs.Responses;
﻿using Asp.Versioning;
using Messenger.API.Extensions;
using Messenger.Application.DTOs.Chats;
using Messenger.Realtime.Hubs;
using Messenger.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.ComponentModel;

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
        private readonly IChatService _chatService;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IUserService _userService;
        private readonly ILogger<ChatsController> _logger;

        public ChatsController(
            IChatService chatService,
            IHubContext<ChatHub> hubContext,
            IUserService userService,
            ILogger<ChatsController> logger)
        {
            _chatService = chatService;
            _hubContext = hubContext;
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
                var chats = await _chatService.GetUserChatsWithLastMessageAsync(userId, ct);

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

                var chat = await _chatService.GetChatByIdAsync(chatId, ct);
                if (chat == null)
                {
                    return NotFound(ApiResponse.Failure("Чат не найден", "NOT_FOUND"));
                }

                var participants = await _chatService.GetChatParticipantsAsync(chatId, ct);
                if (!participants.Any(p => p.UserId == userId))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Failure("Доступ к чату запрещён", "FORBIDDEN"));
                }

                var participantDtos = participants.Select(p =>
                {
                    var role = p.Role;
                    if (string.IsNullOrWhiteSpace(role))
                        role = p.UserId == chat.UserId ? "владелец" : "участник";
                    else if (p.UserId == chat.UserId && role.Trim().ToLowerInvariant() is not ("владелец" or "owner"))
                        role = "владелец";

                    return new
                    {
                        Id = p.UserId,
                        UserId = p.UserId,
                        Name = $"{p.FirstName} {p.LastName}".Trim(),
                        FullName = $"{p.FirstName} {p.LastName}".Trim(),
                        Avatar = p.Avatar,
                        Role = role
                    };
                }).ToList();

                string displayName = chat.Type == "group"
                    ? chat.Name
                    : await GetPrivateChatDisplayNameAsync(chatId, userId, ct);

                var result = new
                {
                    ChatId = chat.ChatId,
                    Name = displayName,
                    Type = chat.Type,
                    UserId = chat.UserId,
                    CreatorId = chat.UserId,
                    Avatar = chat.Type == "group"
                        ? (string?)null
                        : await GetOtherUserAvatarAsync(participantDtos.FirstOrDefault(p => p.UserId != userId)?.UserId ?? userId, ct),
                    Participants = participantDtos
                };

                return Ok(ApiResponse.Success(result));
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
            [FromBody, Description("Данные для создания чата")] CreateChatDto request,
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

                string chatNameForDb = request.Type == "group"
                    ? request.Name.Trim()
                    : "Приватный чат";

                var chat = await _chatService.CreateChatAsync(
                    name: chatNameForDb,
                    type: request.Type,
                    creatorId: currentUserId,
                    ct);

                foreach (var participantId in request.UserIds.Distinct())
                {
                    if (participantId == currentUserId)
                        continue;

                    await _chatService.AddParticipantToChatAsync(chat.ChatId, participantId, "участник", ct);
                }

                string displayName = request.Type == "group"
                    ? request.Name.Trim()
                    : await GetPrivateChatDisplayNameAsync(chat.ChatId, currentUserId, ct);

                foreach (var participantId in request.UserIds.Distinct())
                {
                    var dbParticipant = await _userService.GetUserByIdAsync(participantId, ct);
                    if (dbParticipant == null || string.IsNullOrEmpty(dbParticipant.ExternalId))
                        continue;

                    string individualizedName = request.Type == "group"
                        ? request.Name.Trim()
                        : await GetPrivateChatDisplayNameAsync(chat.ChatId, participantId, ct);

                    string? individualizedAvatar = null;
                    if (request.Type == "private")
                    {
                        var otherId = participantId == currentUserId ? request.UserIds[0] : currentUserId;
                        individualizedAvatar = await GetOtherUserAvatarAsync(otherId, ct);
                    }

                    var chatForSingleUser = new
                    {
                        ChatId = chat.ChatId,
                        Name = individualizedName,
                        Avatar = individualizedAvatar,
                        Type = chat.Type
                    };

                    await _hubContext.Clients.User(dbParticipant.ExternalId.ToLowerInvariant())
                        .SendAsync("NewChat", chatForSingleUser, ct);
                }

                return Ok(ApiResponse.Success(new
                {
                    chat.ChatId,
                    Name = displayName
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
                var currentUserId = await User.GetCurrentUserIdAsync(_userService);

                var chat = await _chatService.GetChatByIdAsync(chatId, ct);
                if (chat == null)
                {
                    return NotFound(ApiResponse.Failure("Чат не найден", "NOT_FOUND"));
                }

                var actor = chat.Participants.FirstOrDefault(p => p.UserId == currentUserId);
                if (actor == null || !IsChatAdministrator(actor.Role))
                    return Forbid();

                if (!IsRegularParticipantRole(role))
                    return BadRequest(ApiResponse.Failure(
                        "Через этот метод можно назначить только роль участника",
                        "INVALID_ROLE"));

                var addedUser = await _userService.GetUserByIdAsync(userId, ct);
                if (addedUser == null)
                    return NotFound(ApiResponse.Failure("Пользователь не найден", "NOT_FOUND"));

                await _chatService.AddParticipantToChatAsync(chatId, userId, "участник", ct);
                if (addedUser == null)
                {
                    return NotFound(ApiResponse.Failure("Пользователь не найден", "NOT_FOUND"));
                }

                var userInfo = new
                {
                    Id = addedUser.UserId,
                    Name = $"{addedUser.FirstName} {addedUser.LastName}".Trim(),
                    FullName = $"{addedUser.FirstName} {addedUser.LastName}".Trim(),
                    Avatar = addedUser.Account?.Avatar
                };

                await _hubContext.Clients.Group(chatId.ToString())
                    .SendAsync("ParticipantAdded", new
                    {
                        ChatId = chatId,
                        User = userInfo
                    }, ct);

                var updatedChat = await _chatService.GetChatByIdAsync(chatId, ct);
                var updatedCount = updatedChat?.Participants?.Count ?? 0;

                await _hubContext.Clients.Group(chatId.ToString())
                    .SendAsync("ParticipantCountChanged", new
                    {
                        ChatId = chatId,
                        Count = updatedCount
                    }, ct);

                var chatForNewUser = new
                {
                    ChatId = chat.ChatId,
                    Name = chat.Type == "group" ? chat.Name : await GetPrivateChatDisplayNameAsync(chatId, userId, ct),
                    Type = chat.Type
                };

                if (!string.IsNullOrEmpty(addedUser.ExternalId))
                {
                    await _hubContext.Clients.User(addedUser.ExternalId.ToLowerInvariant())
                        .SendAsync("NewChat", chatForNewUser, ct);
                }

                return Ok(ApiResponse.Success(message: $"Пользователь {userId} успешно добавлен в чат"));
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
            [FromBody, Description("Новые данные чата")] UpdateChatDto request,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(ApiResponse.Failure("Название не может быть пустым", "INVALID_REQUEST"));
            }

            try
            {
                var currentUserId = await User.GetCurrentUserIdAsync(_userService);

                var chat = await _chatService.GetChatByIdAsync(chatId, ct);
                if (chat == null)
                {
                    return NotFound(ApiResponse.Failure("Чат не найден", "NOT_FOUND"));
                }

                var actor = chat.Participants.FirstOrDefault(p => p.UserId == currentUserId);
                if (actor == null || !IsChatAdministrator(actor.Role))
                    return Forbid();

                if (!string.Equals(chat.Type, "group", StringComparison.OrdinalIgnoreCase))
                    return BadRequest(ApiResponse.Failure(
                        "Переименовывать можно только групповые чаты",
                        "INVALID_CHAT_TYPE"));

                await _chatService.UpdateChatAsync(new UpdateChatDto { ChatId = chatId, Name = request.Name }, ct);

                await _hubContext.Clients.Group(chatId.ToString()).SendAsync("ChatUpdated", new
                {
                    ChatId = chatId,
                    Name = request.Name
                }, ct);

                return Ok(ApiResponse.Success(message: "Чат обновлён"));
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
                var currentUserId = await User.GetCurrentUserIdAsync(_userService);

                var chat = await _chatService.GetChatByIdAsync(chatId, ct);
                if (chat == null)
                {
                    return NotFound(ApiResponse.Failure("Чат не найден", "NOT_FOUND"));
                }

                var actor = chat.Participants.FirstOrDefault(p => p.UserId == currentUserId);
                if (actor == null || !IsChatOwner(actor.Role))
                    return Forbid();

                await _chatService.DeleteChatAsync(chatId, ct);

                await _hubContext.Clients.Group(chatId.ToString()).SendAsync("ChatDeleted", chatId, ct);

                return Ok(ApiResponse.Success(message: "Чат успешно удален"));
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

                var userToDelete = await _userService.GetUserByIdAsync(userId, ct);
                if (userToDelete == null)
                {
                    return NotFound(ApiResponse.Failure("Пользователь не найден", "NOT_FOUND"));
                }

                var participants = (await _chatService.GetChatParticipantsAsync(chatId, ct)).ToList();
                var actor = participants.FirstOrDefault(p => p.UserId == currentUserId);
                if (actor == null)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Failure("Вы не являетесь участником этого чата", "FORBIDDEN"));
                }

                var target = participants.FirstOrDefault(p => p.UserId == userId);
                if (target == null)
                {
                    return NotFound(ApiResponse.Failure("Участник не найден в чате", "NOT_FOUND"));
                }

                static string NormRole(string? role) =>
                    string.IsNullOrWhiteSpace(role) ? "участник" : role.Trim().ToLowerInvariant();

                static bool IsAdminOrOwnerRole(string? role)
                {
                    var r = NormRole(role);
                    return r is "владелец" or "owner" or "admin" or "админ" or "administrator" or "модератор" or "moderator";
                }

                if (userId == currentUserId)
                {
                    return BadRequest(ApiResponse.Failure("Нельзя удалить самого себя из чата этим методом", "INVALID_REQUEST"));
                }

                if (!IsChatAdministrator(actor.Role))
                    return Forbid();

                if (IsAdminOrOwnerRole(target.Role))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new
                    {
                        IsSuccess = false,
                        Error = "Нельзя удалить владельца или администратора чата"
                    });
                }

                await _chatService.DeleteParticipantFromChatAsync(chatId, userId, ct);

                await _hubContext.Clients.Group(chatId.ToString().ToLowerInvariant())
                    .SendAsync("ParticipantRemoved", new
                    {
                        ChatId = chatId,
                        UserId = userId
                    }, ct);

                var updatedChat = await _chatService.GetChatByIdAsync(chatId, ct);
                var updatedCount = updatedChat?.Participants?.Count ?? 0;

                await _hubContext.Clients.Group(chatId.ToString())
                    .SendAsync("ParticipantCountChanged", new
                    {
                        ChatId = chatId,
                        Count = updatedCount
                    }, ct);

                if (!string.IsNullOrEmpty(userToDelete.ExternalId))
                {
                    var signalRId = userToDelete.ExternalId.ToLowerInvariant();

                    await _hubContext.Clients.User(signalRId)
                        .SendAsync("YouWereRemovedFromChat", chatId, ct);
                }

                return Ok(ApiResponse.Success(message: $"Пользователь {userId} успешно удален из чата"));
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

        private static string NormalizeChatRole(string? role) =>
            string.IsNullOrWhiteSpace(role) ? "участник" : role.Trim().ToLowerInvariant();

        private static bool IsChatOwner(string? role) =>
            NormalizeChatRole(role) is "владелец" or "owner";

        private static bool IsChatAdministrator(string? role) =>
            IsChatOwner(role) || NormalizeChatRole(role) is
                "admin" or "админ" or "administrator" or "модератор" or "moderator";

        private static bool IsRegularParticipantRole(string? role) =>
            NormalizeChatRole(role) is "участник" or "member";

        private async Task<string> GetPrivateChatDisplayNameAsync(Guid chatId, Guid currentUserId, CancellationToken ct)
        {
            var participants = await _chatService.GetChatParticipantsAsync(chatId, ct);

            var otherParticipant = participants
                .FirstOrDefault(p => p.UserId != currentUserId);

            if (otherParticipant == null)
                return "Удалённый пользователь";

            return $"{otherParticipant.FirstName} {otherParticipant.LastName}".Trim();
        }

        private async Task<string?> GetOtherUserAvatarAsync(Guid userId, CancellationToken ct)
        {
            var user = await _userService.GetUserByIdAsync(userId, ct);
            return user?.Account?.Avatar;
        }
    }
}