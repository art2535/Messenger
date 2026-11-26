using Messenger.Core.DTOs.Messages;
using Messenger.Core.Interfaces;
using Messenger.Core.Messages;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Services
{
    public sealed class MessageSendingService : IMessageSendingService
    {
        private readonly IUserService _userService;
        private readonly IChatService _chatService;
        private readonly IMessageService _messageService;
        private readonly IEncryptionService _encryptionService;
        private readonly IConfiguration _configuration;
        private readonly IMessageRealtimeNotifier _messageRealtimeNotifier;
        private readonly ILogger<MessageSendingService> _logger;

        public MessageSendingService(
            IUserService userService,
            IChatService chatService,
            IMessageService messageService,
            IEncryptionService encryptionService,
            IConfiguration configuration,
            IMessageRealtimeNotifier messageRealtimeNotifier,
            ILogger<MessageSendingService> logger)
        {
            _userService = userService;
            _chatService = chatService;
            _messageService = messageService;
            _encryptionService = encryptionService;
            _configuration = configuration;
            _messageRealtimeNotifier = messageRealtimeNotifier;
            _logger = logger;
        }

        public async Task<MessageDto> SendAsync(
            Guid chatId,
            Guid senderId,
            string? messageText,
            IFormFile[]? files = null,
            CancellationToken cancellationToken = default)
        {
            var user = await _userService.GetUserByIdAsync(senderId, cancellationToken);
            if (user is null)
            {
                throw new MessageSendingException(
                    MessageSendingFailure.UserNotFound,
                    "Пользователь не найден");
            }

            var chat = await _chatService.GetChatByIdAsync(chatId, cancellationToken);
            if (chat is null)
            {
                throw new MessageSendingException(
                    MessageSendingFailure.ChatNotFound,
                    "Чат не найден");
            }

            if (!chat.ChatParticipants.Any(participant => participant.UserId == user.UserId))
            {
                throw new MessageSendingException(
                    MessageSendingFailure.UserNotParticipant,
                    "Вы не являетесь участником этого чата");
            }

            var attachmentsInfo = new List<AttachmentInfo>();
            var attachmentDtos = new List<AttachmentDto>();

            if (files is { Length: > 0 })
            {
                var uploadPath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    "uploads");

                Directory.CreateDirectory(uploadPath);

                foreach (var file in files.Where(item => item.Length > 0))
                {
                    var fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
                    var filePath = Path.Combine(uploadPath, fileName);
                    var fileUrl = $"{_configuration["URL:API:HTTPS"]}/uploads/{fileName}";

                    await using var stream = new FileStream(
                        filePath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        81920,
                        FileOptions.Asynchronous);

                    await file.CopyToAsync(stream, cancellationToken);

                    var attachmentId = Guid.NewGuid();
                    var fileType = file.ContentType ?? "application/octet-stream";

                    attachmentsInfo.Add(new AttachmentInfo
                    {
                        AttachmentId = attachmentId,
                        FileName = file.FileName,
                        FileType = fileType,
                        SizeInBytes = file.Length,
                        Url = fileUrl
                    });

                    attachmentDtos.Add(new AttachmentDto
                    {
                        AttachmentId = attachmentId,
                        FileName = file.FileName,
                        FileType = fileType,
                        SizeInBytes = (int)file.Length,
                        Url = fileUrl
                    });
                }
            }

            var messageId = Guid.NewGuid();
            var encryptedText = string.IsNullOrWhiteSpace(messageText)
                ? null
                : _encryptionService.Encrypt(messageText.Trim());

            await _messageService.PublishChatMessageAsync(
                new ChatMessageSent
                {
                    MessageId = messageId,
                    ChatId = chatId,
                    SenderId = user.UserId,
                    SenderName = $"{user.FirstName} {user.LastName}".Trim(),
                    MessageText = encryptedText,
                    SentAt = UtcDateTime.AsUtc(UtcDateTime.Now),
                    HasAttachments = attachmentsInfo.Count > 0,
                    Attachments = attachmentsInfo
                },
                cancellationToken);

            var finalMessageDto = new MessageDto
            {
                MessageId = messageId,
                ChatId = chatId,
                SenderId = user.UserId,
                SenderName = $"{user.FirstName} {user.LastName}".Trim(),
                MessageText = string.IsNullOrEmpty(encryptedText)
                    ? null
                    : _encryptionService.TryDecryptSafe(encryptedText),
                SentAt = UtcDateTime.AsUtc(UtcDateTime.Now),
                Status = "Sent",
                Attachments = attachmentDtos
            };

            try
            {
                await _messageRealtimeNotifier.SendMessagePublishedAsync(
                    chatId,
                    finalMessageDto,
                    messageId,
                    chat.ChatParticipants.Select(participant => participant.UserId),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Сообщение {MessageId} опубликовано, но SignalR-рассылка не удалась",
                    messageId);
            }

            return finalMessageDto;
        }
    }
}
