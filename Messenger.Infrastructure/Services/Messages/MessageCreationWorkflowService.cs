using Messenger.Core.DTOs;
using Messenger.Core.Models;

namespace Messenger.Infrastructure.Services.Messages
{
    public sealed class MessageCreationWorkflowService : IMessageCreationWorkflowService
    {
        private readonly IMessageCreationService _messageCreationService;
        private readonly IMessageLookupService _messageLookupService;

        public MessageCreationWorkflowService(
            IMessageCreationService messageCreationService,
            IMessageLookupService messageLookupService)
        {
            _messageCreationService = messageCreationService;
            _messageLookupService = messageLookupService;
        }

        public async Task<ServiceResult<Message>> SendMessageAsync(
            Guid messageId,
            Guid chatId,
            Guid senderId,
            string? content,
            bool hasAttachments,
            CancellationToken token = default)
        {
            try
            {
                var message = new Message
                {
                    MessageId = messageId,
                    ChatId = chatId,
                    SenderId = senderId,
                    MessageText = content ?? string.Empty,
                    HasAttachments = hasAttachments,
                    SendTime = UtcDateTime.Now,
                    DeliveryStatus = MessageDeliveryStatus.Pending
                };

                await _messageCreationService.CreateMessageAsync(message, token);

                var savedMessage = await _messageLookupService.GetMessageByIdAsync(
                    chatId,
                    message.MessageId,
                    token);

                return savedMessage != null
                    ? ServiceResult<Message>.Success(savedMessage)
                    : ServiceResult<Message>.Failure("Не удалось сохранить сообщение");
            }
            catch (Exception exception)
            {
                return ServiceResult<Message>.Failure(
                    exception.Message,
                    exception.InnerException?.Message);
            }
        }
    }
}
