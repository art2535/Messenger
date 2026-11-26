using Messenger.Core.DTOs.Chats;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Core.Hubs;
using Messenger.Infrastructure.Interfaces.Chats;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.Infrastructure.Services.Chats
{
    public sealed class ChatManagementWorkflowService : IChatManagementWorkflowService
    {
        private readonly IChatService _chatService;
        private readonly IUserService _userService;
        private readonly IHubContext<ChatHub> _hubContext;

        public ChatManagementWorkflowService(
            IChatService chatService,
            IUserService userService,
            IHubContext<ChatHub> hubContext)
        {
            _chatService = chatService;
            _userService = userService;
            _hubContext = hubContext;
        }

        public async Task<ChatCreationWorkflowResult> CreateChatAsync(
            CreateChatRequest request,
            Guid creatorId,
            CancellationToken ct = default)
        {
            string chatNameForDb = request.Type == "group"
                ? request.Name!.Trim()
                : "Приватный чат";

            var chat = await _chatService.CreateChatAsync(
                chatNameForDb,
                request.Type,
                creatorId,
                ct);

            foreach (var participantId in request.UserIds.Distinct())
            {
                if (participantId == creatorId)
                {
                    continue;
                }

                await _chatService.AddParticipantToChatAsync(
                    chat.ChatId,
                    participantId,
                    "участник",
                    ct);
            }

            string displayName = request.Type == "group"
                ? request.Name!.Trim()
                : await GetPrivateChatDisplayNameAsync(chat.ChatId, creatorId, ct);

            foreach (var participantId in request.UserIds.Distinct())
            {
                var dbParticipant = await _userService.GetUserByIdAsync(participantId, ct);
                if (dbParticipant == null || string.IsNullOrEmpty(dbParticipant.ExternalId))
                {
                    continue;
                }

                string individualizedName = request.Type == "group"
                    ? request.Name!.Trim()
                    : await GetPrivateChatDisplayNameAsync(chat.ChatId, participantId, ct);

                string? individualizedAvatar = null;
                if (request.Type == "private")
                {
                    var otherId = participantId == creatorId
                        ? request.UserIds[0]
                        : creatorId;
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

            return new ChatCreationWorkflowResult(chat.ChatId, displayName);
        }


        public async Task<ChatManagementWorkflowResult> AddParticipantAsync(
            Guid chatId,
            Guid userId,
            string role,
            CancellationToken ct = default)
        {
            var chat = await _chatService.GetChatByIdAsync(chatId, ct);
            if (chat == null)
            {
                return new ChatManagementWorkflowResult(
                    ChatManagementStatus.NotFound, "Чат не найден");
            }

            await _chatService.AddParticipantToChatAsync(chatId, userId, role, ct);

            var addedUser = await _userService.GetUserByIdAsync(userId, ct);
            if (addedUser == null)
            {
                return new ChatManagementWorkflowResult(
                    ChatManagementStatus.NotFound, "Пользователь не найден");
            }

            var userInfo = new
            {
                Id = addedUser.UserId,
                Name = $"{addedUser.FirstName} {addedUser.LastName}".Trim(),
                FullName = $"{addedUser.FirstName} {addedUser.LastName}".Trim(),
                Avatar = addedUser.Account?.Avatar
            };

            await _hubContext.Clients.Group(chatId.ToString())
                .SendAsync("ParticipantAdded", new { ChatId = chatId, User = userInfo }, ct);

            var updatedChat = await _chatService.GetChatByIdAsync(chatId, ct);
            var updatedCount = updatedChat?.ChatParticipants?.Count ?? 0;

            await _hubContext.Clients.Group(chatId.ToString())
                .SendAsync("ParticipantCountChanged", new
                {
                    ChatId = chatId,
                    Count = updatedCount
                }, ct);

            var chatForNewUser = new
            {
                ChatId = chat.ChatId,
                Name = chat.Type == "group"
                    ? chat.Name
                    : await GetPrivateChatDisplayNameAsync(chatId, userId, ct),
                Type = chat.Type
            };

            if (!string.IsNullOrEmpty(addedUser.ExternalId))
            {
                await _hubContext.Clients.User(addedUser.ExternalId.ToLowerInvariant())
                    .SendAsync("NewChat", chatForNewUser, ct);
            }

            return new ChatManagementWorkflowResult(
                ChatManagementStatus.Success,
                $"Пользователь {userId} успешно добавлен в чат");
        }

        public async Task<ChatManagementWorkflowResult> UpdateChatAsync(
            Guid chatId,
            string name,
            CancellationToken ct = default)
        {
            var chat = await _chatService.GetChatByIdAsync(chatId, ct);
            if (chat == null)
            {
                return new ChatManagementWorkflowResult(
                    ChatManagementStatus.NotFound, "Чат не найден");
            }

            chat.Name = name;
            await _chatService.UpdateChatAsync(chat, ct);

            await _hubContext.Clients.Group(chatId.ToString()).SendAsync("ChatUpdated", new
            {
                ChatId = chatId,
                Name = name
            }, ct);

            return new ChatManagementWorkflowResult(ChatManagementStatus.Success, "Чат обновлён");
        }

        public async Task<ChatManagementWorkflowResult> DeleteChatAsync(
            Guid chatId,
            CancellationToken ct = default)
        {
            var chat = await _chatService.GetChatByIdAsync(chatId, ct);
            if (chat == null)
            {
                return new ChatManagementWorkflowResult(
                    ChatManagementStatus.NotFound, "Чат не найден");
            }

            await _chatService.DeleteChatAsync(chat, ct);
            await _hubContext.Clients.Group(chatId.ToString()).SendAsync("ChatDeleted", chatId, ct);

            return new ChatManagementWorkflowResult(
                ChatManagementStatus.Success, "Чат успешно удален");
        }

        public async Task<ChatManagementWorkflowResult> RemoveParticipantAsync(
            Guid chatId,
            Guid userId,
            Guid currentUserId,
            CancellationToken ct = default)
        {
            var userToDelete = await _userService.GetUserByIdAsync(userId, ct);
            if (userToDelete == null)
            {
                return new ChatManagementWorkflowResult(
                    ChatManagementStatus.NotFound, "Пользователь не найден");
            }

            var participants = (await _chatService.GetChatParticipantsAsync(chatId, ct)).ToList();
            var actor = participants.FirstOrDefault(p => p.UserId == currentUserId);
            if (actor == null)
            {
                return new ChatManagementWorkflowResult(
                    ChatManagementStatus.Forbidden,
                    "Вы не являетесь участником этого чата");
            }

            var target = participants.FirstOrDefault(p => p.UserId == userId);
            if (target == null)
            {
                return new ChatManagementWorkflowResult(
                    ChatManagementStatus.NotFound, "Участник не найден в чате");
            }

            static string NormalizeRole(string? role) =>
                string.IsNullOrWhiteSpace(role) ? "участник" : role.Trim().ToLowerInvariant();

            static bool IsAdminOrOwnerRole(string? role)
            {
                var normalizedRole = NormalizeRole(role);
                return normalizedRole is "владелец" or "owner" or "admin" or "админ"
                    or "administrator" or "модератор" or "moderator";
            }

            if (userId == currentUserId)
            {
                return new ChatManagementWorkflowResult(
                    ChatManagementStatus.BadRequest,
                    "Нельзя удалить самого себя из чата этим методом");
            }

            if (IsAdminOrOwnerRole(target.Role))
            {
                string error = IsAdminOrOwnerRole(actor.Role)
                    ? "Администратор не может удалить владельца или другого администратора"
                    : "Нельзя удалить владельца или администратора чата";

                return new ChatManagementWorkflowResult(
                    ChatManagementStatus.Forbidden,
                    error,
                    UseLegacyErrorResponse: true);
            }

            await _chatService.DeleteParticipantFromChatAsync(chatId, userId, ct);

            await _hubContext.Clients.Group(chatId.ToString().ToLowerInvariant())
                .SendAsync("ParticipantRemoved", new
                {
                    ChatId = chatId,
                    UserId = userId
                }, ct);

            var updatedChat = await _chatService.GetChatByIdAsync(chatId, ct);
            var updatedCount = updatedChat?.ChatParticipants?.Count ?? 0;

            await _hubContext.Clients.Group(chatId.ToString())
                .SendAsync("ParticipantCountChanged", new
                {
                    ChatId = chatId,
                    Count = updatedCount
                }, ct);

            if (!string.IsNullOrEmpty(userToDelete.ExternalId))
            {
                await _hubContext.Clients.User(userToDelete.ExternalId.ToLowerInvariant())
                    .SendAsync("YouWereRemovedFromChat", chatId, ct);
            }

            return new ChatManagementWorkflowResult(
                ChatManagementStatus.Success,
                $"Пользователь {userId} успешно удален из чата");
        }

        private async Task<string> GetPrivateChatDisplayNameAsync(
            Guid chatId,
            Guid currentUserId,
            CancellationToken ct)
        {
            var participants = await _chatService.GetChatParticipantsAsync(chatId, ct);
            var otherParticipant = participants.FirstOrDefault(p => p.UserId != currentUserId);

            if (otherParticipant?.User == null)
            {
                return "Удалённый пользователь";
            }

            return $"{otherParticipant.User.FirstName} {otherParticipant.User.LastName}".Trim();
        }

        private async Task<string?> GetOtherUserAvatarAsync(Guid userId, CancellationToken ct)
        {
            var user = await _userService.GetUserByIdAsync(userId, ct);
            return user?.Account?.Avatar;
        }
    }
}
