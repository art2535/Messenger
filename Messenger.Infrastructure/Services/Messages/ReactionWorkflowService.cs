using Messenger.Core.Hubs;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Interfaces.Messages;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.Infrastructure.Services.Messages
{
    public sealed class ReactionWorkflowService : IReactionWorkflowService
    {
        private readonly IReactionService _reactionService;
        private readonly IHubContext<ChatHub> _hubContext;

        public ReactionWorkflowService(
            IReactionService reactionService,
            IHubContext<ChatHub> hubContext)
        {
            _reactionService = reactionService;
            _hubContext = hubContext;
        }

        public Task<IEnumerable<Reaction>> GetReactionsByMessageIdAsync(
            Guid messageId,
            CancellationToken cancellationToken = default)
        {
            return _reactionService.GetReactionsByMessageIdAsync(messageId, cancellationToken);
        }

        public async Task AddReactionAsync(
            Guid messageId,
            User user,
            string reactionType,
            CancellationToken cancellationToken = default)
        {
            var reaction = new Reaction
            {
                ReactionId = Guid.NewGuid(),
                MessageId = messageId,
                UserId = user.UserId,
                ReactionType = reactionType.Trim()
            };

            await _reactionService.AddReactionAsync(reaction, cancellationToken);

            var chatId = await _reactionService.GetChatIdByMessageIdAsync(
                messageId,
                cancellationToken);

            if (!chatId.HasValue)
            {
                return;
            }

            await _hubContext.Clients.Group(chatId.Value.ToString())
                .SendAsync("ReactionUpdated", new
                {
                    MessageId = messageId,
                    ChatId = chatId.Value,
                    UserId = user.UserId,
                    UserName = $"{user.FirstName} {user.LastName}".Trim(),
                    ReactionType = reaction.ReactionType,
                    Action = "add"
                }, cancellationToken);
        }

        public async Task DeleteReactionAsync(
            Guid messageId,
            User user,
            CancellationToken cancellationToken = default)
        {
            if (!await _reactionService.MessageExistsAsync(messageId, cancellationToken))
            {
                throw new KeyNotFoundException("Сообщение не найдено");
            }

            await _reactionService.DeleteReactionAsync(
                messageId,
                user.UserId,
                cancellationToken);

            var chatId = await _reactionService.GetChatIdByMessageIdAsync(
                messageId,
                cancellationToken);

            if (!chatId.HasValue)
            {
                return;
            }

            await _hubContext.Clients.Group(chatId.Value.ToString())
                .SendAsync("ReactionUpdated", new
                {
                    MessageId = messageId,
                    ChatId = chatId.Value,
                    UserId = user.UserId,
                    UserName = $"{user.FirstName} {user.LastName}".Trim(),
                    ReactionType = (string?)null,
                    Action = "remove"
                }, cancellationToken);
        }
    }
}
