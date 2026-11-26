using Messenger.Core.DTOs.Messages;
using Messenger.Core.Hubs;
using Messenger.Core.Interfaces;
using Messenger.Core.Messages;
using Messenger.Core.Models;
using Messenger.Infrastructure.Interfaces.Messages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Services.Messages
{
    public sealed class ScheduledMessageWorkflowService : IScheduledMessageWorkflowService
    {
        private const long MaxFileSize = 10 * 1024 * 1024;

        private readonly IScheduledMessageService _scheduledService;
        private readonly IChatService _chatService;
        private readonly IEncryptionService _encryption;
        private readonly IConfiguration _configuration;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ILogger<ScheduledMessageWorkflowService> _logger;

        public ScheduledMessageWorkflowService(
            IScheduledMessageService scheduledService,
            IChatService chatService,
            IEncryptionService encryption,
            IConfiguration configuration,
            IHubContext<ChatHub> hubContext,
            ILogger<ScheduledMessageWorkflowService> logger)
        {
            _scheduledService = scheduledService;
            _chatService = chatService;
            _encryption = encryption;
            _configuration = configuration;
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task<ScheduledMessageDto> ScheduleAsync(
            User user,
            Guid chatId,
            string? messageText,
            DateTimeOffset? scheduledAt,
            string? replyToMessageId,
            IFormFile[]? files,
            CancellationToken cancellationToken = default)
        {
            if (!scheduledAt.HasValue)
            {
                throw new ArgumentException("Не указано время отправки (scheduledAt)");
            }

            var scheduledUtc = scheduledAt.Value.UtcDateTime;
            if (scheduledUtc <= DateTime.UtcNow.AddSeconds(30))
            {
                throw new ArgumentException(
                    "Время отправки должно быть хотя бы на 30 секунд в будущем");
            }

            var chat = await _chatService.GetChatByIdAsync(chatId, cancellationToken);
            if (chat == null)
            {
                throw new ScheduledMessageChatNotFoundException("Чат не найден");
            }

            if (!chat.ChatParticipants.Any(participant => participant.UserId == user.UserId))
            {
                throw new ScheduledMessageForbiddenException("Доступ запрещён");
            }

            if (files != null)
            {
                foreach (var file in files)
                {
                    if (file.Length > MaxFileSize)
                    {
                        throw new ArgumentException(
                            $"Файл '{file.FileName}' превышает 10 МБ");
                    }
                }
            }

            var attachmentsInfo = await SaveAttachmentsAsync(files, cancellationToken);
            if (string.IsNullOrWhiteSpace(messageText) && attachmentsInfo.Count == 0)
            {
                throw new ArgumentException("Пустое сообщение");
            }

            var encrypted = string.IsNullOrWhiteSpace(messageText)
                ? null
                : _encryption.Encrypt(messageText.Trim());

            var entity = await _scheduledService.CreateAsync(
                chatId,
                user.UserId,
                encrypted,
                attachmentsInfo.Count > 0,
                attachmentsInfo,
                scheduledUtc,
                replyToMessageId,
                cancellationToken);

            var senderName = $"{user.FirstName} {user.LastName}".Trim();
            var dto = _scheduledService.ToDto(
                entity,
                messageText?.Trim(),
                senderName,
                chat.Name);

            await SendRealtimeAsync(
                user.UserId,
                "ScheduledMessageCreated",
                dto,
                cancellationToken);

            return dto;
        }

        public async Task<List<ScheduledMessageDto>> GetPendingByUserAsync(
            User user,
            CancellationToken cancellationToken = default)
        {
            var list = await _scheduledService.GetPendingByUserAsync(
                user.UserId,
                cancellationToken);

            return list.Select(entity =>
            {
                var plain = string.IsNullOrEmpty(entity.MessageText)
                    ? null
                    : _encryption.TryDecryptSafe(entity.MessageText);

                return _scheduledService.ToDto(entity, plain, null, entity.Chat?.Name);
            }).ToList();
        }

        public async Task<List<ScheduledMessageDto>> GetPendingByChatAsync(
            User user,
            Guid chatId,
            CancellationToken cancellationToken = default)
        {
            var list = await _scheduledService.GetPendingByChatAsync(
                chatId,
                user.UserId,
                cancellationToken);

            return list.Select(entity =>
            {
                var plain = string.IsNullOrEmpty(entity.MessageText)
                    ? null
                    : _encryption.TryDecryptSafe(entity.MessageText);

                return _scheduledService.ToDto(entity, plain);
            }).ToList();
        }

        public async Task<ScheduledMessageDto?> UpdateAsync(
            User user,
            Guid scheduledMessageId,
            UpdateScheduledMessageRequest request,
            CancellationToken cancellationToken = default)
        {
            string? encrypted = null;
            if (request.MessageText != null)
            {
                encrypted = string.IsNullOrWhiteSpace(request.MessageText)
                    ? string.Empty
                    : _encryption.Encrypt(request.MessageText.Trim());
            }

            DateTime? scheduledAt = request.ScheduledAt?.UtcDateTime;
            var entity = await _scheduledService.UpdateAsync(
                scheduledMessageId,
                user.UserId,
                encrypted,
                scheduledAt,
                cancellationToken);

            if (entity == null)
            {
                return null;
            }

            var plain = request.MessageText?.Trim()
                ?? (string.IsNullOrEmpty(entity.MessageText)
                    ? null
                    : _encryption.TryDecryptSafe(entity.MessageText));

            var dto = _scheduledService.ToDto(
                entity,
                plain,
                $"{user.FirstName} {user.LastName}".Trim());

            await SendRealtimeAsync(
                user.UserId,
                "ScheduledMessageUpdated",
                dto,
                cancellationToken);

            return dto;
        }

        public async Task<bool> CancelAsync(
            User user,
            Guid scheduledMessageId,
            CancellationToken cancellationToken = default)
        {
            var cancelled = await _scheduledService.CancelAsync(
                scheduledMessageId,
                user.UserId,
                cancellationToken);

            if (!cancelled)
            {
                return false;
            }

            await SendRealtimeAsync(
                user.UserId,
                "ScheduledMessageCancelled",
                new { ScheduledMessageId = scheduledMessageId },
                cancellationToken);

            return true;
        }

        private async Task<List<AttachmentInfo>> SaveAttachmentsAsync(
            IFormFile[]? files,
            CancellationToken cancellationToken)
        {
            var attachmentsInfo = new List<AttachmentInfo>();
            if (files == null || files.Length == 0)
            {
                return attachmentsInfo;
            }

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

                await using var stream = new FileStream(filePath, FileMode.Create);
                await file.CopyToAsync(stream, cancellationToken);

                attachmentsInfo.Add(new AttachmentInfo
                {
                    AttachmentId = Guid.NewGuid(),
                    FileName = file.FileName,
                    FileType = file.ContentType ?? "application/octet-stream",
                    SizeInBytes = file.Length,
                    Url = fileUrl
                });
            }

            return attachmentsInfo;
        }

        private async Task SendRealtimeAsync(
            Guid userId,
            string eventName,
            object payload,
            CancellationToken cancellationToken)
        {
            try
            {
                await _hubContext.Clients.Group($"User_{userId}")
                    .SendAsync(eventName, payload, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SignalR {EventName} failed", eventName);
            }
        }
    }
}
