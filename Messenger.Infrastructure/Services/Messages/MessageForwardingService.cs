using Messenger.Core.DTOs.Messages;
using Messenger.Core.Interfaces;
using Messenger.Core.Messages;
using Messenger.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Services.MessageForwardingService.cs
{
    public sealed class MessageForwardingService : IMessageForwardingService
    {
        private readonly IUserService _userService;
        private readonly IChatService _chatService;
        private readonly IMessageService _messageService;
        private readonly IAttachmentService _attachmentService;
        private readonly IEncryptionService _encryptionService;
        private readonly IConfiguration _configuration;
        private readonly IMessageRealtimeNotifier _messageRealtimeNotifier;
        private readonly ILogger<MessageForwardingService> _logger;

        public MessageForwardingService(
            IUserService userService,
            IChatService chatService,
            IMessageService messageService,
            IAttachmentService attachmentService,
            IEncryptionService encryptionService,
            IConfiguration configuration,
            IMessageRealtimeNotifier messageRealtimeNotifier,
            ILogger<MessageForwardingService> logger)
        {
            _userService = userService;
            _chatService = chatService;
            _messageService = messageService;
            _attachmentService = attachmentService;
            _encryptionService = encryptionService;
            _configuration = configuration;
            _messageRealtimeNotifier = messageRealtimeNotifier;
            _logger = logger;
        }

        public async Task<MessageForwardingResult> ForwardAsync(
            Guid senderId,
            IReadOnlyCollection<Guid> messageIds,
            IReadOnlyCollection<Guid> targetChatIds,
            CancellationToken cancellationToken = default)
        {
            var user = await _userService.GetUserByIdAsync(senderId, cancellationToken);
            if (user is null)
            {
                return Failure(
                    MessageForwardingFailure.UserNotFound,
                    "Пользователь не найден");
            }

            var sourceMessages = new List<ForwardSourceMessage>();
            foreach (var messageId in messageIds)
            {
                var message = await _messageService.GetMessageByIdGlobalAsync(
                    messageId,
                    cancellationToken);

                if (message is null)
                {
                    return Failure(
                        MessageForwardingFailure.SourceMessageNotFound,
                        $"Сообщение {messageId} не найдено");
                }

                var sourceChat = await _chatService.GetChatByIdAsync(
                    message.ChatId,
                    cancellationToken);

                if (sourceChat is null
                    || !sourceChat.ChatParticipants.Any(participant => participant.UserId == user.UserId))
                {
                    return Failure(
                        MessageForwardingFailure.SourceChatAccessDenied,
                        "Нет доступа к исходному чату");
                }

                var plainText = string.IsNullOrEmpty(message.MessageText)
                    ? null
                    : _encryptionService.TryDecryptSafe(message.MessageText);

                var attachments = message.Attachments?.ToList()
                    ?? (await _attachmentService.GetAttachmentsByMessageIdAsync(
                        messageId,
                        cancellationToken)).ToList();

                sourceMessages.Add(new ForwardSourceMessage(message, plainText, attachments));
            }

            var targetChats = new List<Chat>();
            foreach (var targetChatId in targetChatIds)
            {
                var chat = await _chatService.GetChatByIdAsync(
                    targetChatId,
                    cancellationToken);

                if (chat is null)
                {
                    return Failure(
                        MessageForwardingFailure.TargetChatNotFound,
                        $"Чат {targetChatId} не найден");
                }

                if (!chat.ChatParticipants.Any(participant => participant.UserId == user.UserId))
                {
                    return Failure(
                        MessageForwardingFailure.TargetChatAccessDenied,
                        $"Нет доступа к целевому чату {targetChatId}");
                }

                targetChats.Add(chat);
            }

            var senderName = $"{user.FirstName} {user.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(senderName))
            {
                senderName = "Пользователь";
            }

            var uploadPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "uploads");

            Directory.CreateDirectory(uploadPath);
            var apiBase = (_configuration["URL:API:HTTPS"] ?? string.Empty).TrimEnd('/');
            var published = 0;
            var errors = new List<string>();

            foreach (var target in targetChats)
            {
                foreach (var source in sourceMessages)
                {
                    try
                    {
                        await ForwardMessageAsync(
                            source,
                            target,
                            user.UserId,
                            senderName,
                            uploadPath,
                            apiBase,
                            cancellationToken);

                        published++;
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(
                            exception,
                            "Ошибка пересылки сообщения {MessageId} в чат {ChatId}",
                            source.Message.MessageId,
                            target.ChatId);

                        errors.Add(exception.Message);
                    }
                }
            }

            if (published == 0)
            {
                return new MessageForwardingResult
                {
                    Failure = MessageForwardingFailure.NothingPublished,
                    ErrorMessage = errors.FirstOrDefault()
                        ?? "Не удалось переслать сообщения",
                    Errors = errors
                };
            }

            return new MessageForwardingResult
            {
                Published = published,
                Errors = errors
            };
        }

        private async Task ForwardMessageAsync(
            ForwardSourceMessage source,
            Chat target,
            Guid senderId,
            string senderName,
            string uploadPath,
            string apiBase,
            CancellationToken cancellationToken)
        {
            var originalSender = source.Message.Sender is null
                ? "Пользователь"
                : $"{source.Message.Sender.FirstName} {source.Message.Sender.LastName}".Trim();

            if (string.IsNullOrWhiteSpace(originalSender))
            {
                originalSender = "Пользователь";
            }

            var forwardHeader = "\u200BFORWARD:"
                + source.Message.MessageId
                + "|"
                + originalSender.Replace("|", " ")
                + "\u200B\n";

            var contentToEncrypt = forwardHeader + (source.PlainText ?? string.Empty);
            var encryptedText = string.IsNullOrWhiteSpace(contentToEncrypt)
                ? null
                : _encryptionService.Encrypt(contentToEncrypt.TrimEnd());

            var newMessageId = Guid.NewGuid();
            var attachmentsInfo = new List<AttachmentInfo>();

            foreach (var attachment in source.Attachments)
            {
                var diskName = ExtractUploadFileName(attachment.Url, attachment.FileName);
                var sourcePath = Path.Combine(uploadPath, diskName);
                if (!File.Exists(sourcePath))
                {
                    _logger.LogWarning("Файл вложения не найден: {Path}", sourcePath);
                    continue;
                }

                var extension = Path.GetExtension(diskName);
                if (string.IsNullOrEmpty(extension) && !string.IsNullOrEmpty(attachment.FileName))
                {
                    extension = Path.GetExtension(attachment.FileName);
                }

                var newFileName = Guid.NewGuid() + extension;
                var destinationPath = Path.Combine(uploadPath, newFileName);

                await using (var sourceStream = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    FileOptions.Asynchronous))
                await using (var destinationStream = new FileStream(
                    destinationPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous))
                {
                    await sourceStream.CopyToAsync(destinationStream, cancellationToken);
                }

                var attachmentId = Guid.NewGuid();
                var fileUrl = $"{apiBase}/uploads/{newFileName}";
                var size = attachment.SizeInBytes ?? (int)new FileInfo(destinationPath).Length;

                attachmentsInfo.Add(new AttachmentInfo
                {
                    AttachmentId = attachmentId,
                    FileName = attachment.FileName,
                    FileType = attachment.FileType ?? "application/octet-stream",
                    SizeInBytes = size,
                    Url = fileUrl
                });
            }

            await _messageService.PublishChatMessageAsync(
                new ChatMessageSent
                {
                    MessageId = newMessageId,
                    ChatId = target.ChatId,
                    SenderId = senderId,
                    SenderName = senderName,
                    MessageText = encryptedText,
                    SentAt = UtcDateTime.AsUtc(UtcDateTime.Now),
                    HasAttachments = attachmentsInfo.Count > 0,
                    Attachments = attachmentsInfo
                },
                cancellationToken);

            var forwardDto = new MessageDto
            {
                MessageId = newMessageId,
                ChatId = target.ChatId,
                SenderId = senderId,
                SenderName = senderName,
                MessageText = string.IsNullOrEmpty(encryptedText)
                    ? null
                    : _encryptionService.TryDecryptSafe(encryptedText),
                SentAt = UtcDateTime.AsUtc(UtcDateTime.Now),
                Status = "Sent",
                Attachments = attachmentsInfo.Select(attachment => new AttachmentDto
                {
                    AttachmentId = attachment.AttachmentId,
                    FileName = attachment.FileName,
                    FileType = attachment.FileType,
                    SizeInBytes = (int)attachment.SizeInBytes,
                    Url = attachment.Url
                }).ToList()
            };

            try
            {
                await _messageRealtimeNotifier.SendMessagePublishedAsync(
                    target.ChatId,
                    forwardDto,
                    newMessageId,
                    target.ChatParticipants.Select(participant => participant.UserId),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "SignalR после forward не удался MessageId={MessageId} ChatId={ChatId}",
                    newMessageId,
                    target.ChatId);
            }
        }

        private static MessageForwardingResult Failure(
            MessageForwardingFailure failure,
            string message)
        {
            return new MessageForwardingResult
            {
                Failure = failure,
                ErrorMessage = message
            };
        }

        private static string ExtractUploadFileName(string? url, string? fileName)
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    var path = url;
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    {
                        path = uri.AbsolutePath;
                    }

                    var name = Path.GetFileName(path.Split('?')[0]);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }
                catch
                {
                    // Если URL некорректен, используем исходное имя файла.
                }
            }

            return !string.IsNullOrWhiteSpace(fileName)
                ? Path.GetFileName(fileName)
                : "file";
        }

        private sealed record ForwardSourceMessage(
            Message Message,
            string? PlainText,
            List<Attachment> Attachments);
    }
}
