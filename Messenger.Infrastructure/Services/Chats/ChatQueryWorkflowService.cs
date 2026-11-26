using Messenger.Core.Interfaces;
using Messenger.Infrastructure.Interfaces.Chats;

namespace Messenger.Infrastructure.Services.ChatQueryWorkflowService.cs.Chats
{
    public sealed class ChatQueryWorkflowService : IChatQueryWorkflowService
    {
        private readonly IChatService _chatService;
        private readonly IUserService _userService;

        public ChatQueryWorkflowService(IChatService chatService, IUserService userService)
        {
            _chatService = chatService;
            _userService = userService;
        }

        public Task<List<object>> GetUserChatsAsync(Guid userId, CancellationToken ct = default)
        {
            return _chatService.GetUserChatsWithLastMessageAsync(userId, ct);
        }

        public async Task<ChatQueryResult> GetChatDetailsAsync(
            Guid chatId,
            Guid userId,
            CancellationToken ct = default)
        {
            var chat = await _chatService.GetChatByIdAsync(chatId, ct);
            if (chat == null)
            {
                return new ChatQueryResult(
                    ChatQueryStatus.NotFound,
                    Error: "Чат не найден");
            }

            var participants = await _chatService.GetChatParticipantsAsync(chatId, ct);
            if (!participants.Any(p => p.UserId == userId))
            {
                return new ChatQueryResult(
                    ChatQueryStatus.Forbidden,
                    Error: "Доступ к чату запрещён");
            }

            var participantDtos = participants.Select(p =>
            {
                var role = p.Role;
                if (string.IsNullOrWhiteSpace(role))
                {
                    role = p.UserId == chat.UserId ? "владелец" : "участник";
                }
                else if (p.UserId == chat.UserId &&
                    role.Trim().ToLowerInvariant() is not ("владелец" or "owner"))
                {
                    role = "владелец";
                }

                return new
                {
                    Id = p.UserId,
                    UserId = p.UserId,
                    Name = $"{p.User?.FirstName} {p.User?.LastName}".Trim(),
                    FullName = $"{p.User?.FirstName} {p.User?.LastName}".Trim(),
                    Avatar = p.User?.Account?.Avatar,
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
                    : await GetOtherUserAvatarAsync(
                        participantDtos.FirstOrDefault(p => p.UserId != userId)?.UserId ?? userId,
                        ct),
                Participants = participantDtos
            };

            return new ChatQueryResult(ChatQueryStatus.Success, result);
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
