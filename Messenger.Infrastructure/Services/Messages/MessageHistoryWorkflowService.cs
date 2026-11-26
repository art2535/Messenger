using Messenger.Core.DTOs;
using Messenger.Core.DTOs.Messages;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;

namespace Messenger.Infrastructure.Services.MessageHistoryWorkflowService.cs
{
    public sealed class MessageHistoryWorkflowService : IMessageHistoryWorkflowService
    {
        private readonly IMessagePaginationService _paginationService;
        private readonly IMessageChatExportService _chatExportService;
        private readonly IChatService _chatService;
        private readonly IEncryptionService _encryptionService;

        public MessageHistoryWorkflowService(
            IMessagePaginationService paginationService,
            IMessageChatExportService chatExportService,
            IChatService chatService,
            IEncryptionService encryptionService)
        {
            _paginationService = paginationService;
            _chatExportService = chatExportService;
            _chatService = chatService;
            _encryptionService = encryptionService;
        }

        public async Task<MessageHistoryResult> GetMessagesAsync(
            Guid chatId,
            long? beforeSequence,
            int limit,
            CancellationToken cancellationToken = default)
        {
            var (messages, hasMore) = await _paginationService.GetMessagesByChatIdPagedAsync(
                chatId,
                beforeSequence,
                limit,
                cancellationToken);

            var dtos = messages.Select(message => new MessageDto
            {
                MessageId = message.MessageId,
                ChatId = message.ChatId == Guid.Empty ? chatId : message.ChatId,
                SenderId = message.SenderId,
                SenderName = message.Sender != null
                    ? $"{message.Sender.FirstName} {message.Sender.LastName}".Trim()
                    : "Удалённый пользователь",
                MessageText = string.IsNullOrEmpty(message.MessageText)
                    ? null
                    : _encryptionService.TryDecryptSafe(message.MessageText),
                SentAt = UtcDateTime.AsUtc(message.SendTime),
                SequenceNumber = message.SequenceNumber,
                Status = message.DeliveryStatus switch
                {
                    MessageDeliveryStatus.Pending => "Pending",
                    MessageDeliveryStatus.Sent => "Sent",
                    MessageDeliveryStatus.Delivered => "Delivered",
                    MessageDeliveryStatus.Read => "Read",
                    MessageDeliveryStatus.Failed => "Failed",
                    _ => "Invalid status"
                },
                Attachments = message.Attachments.Select(attachment => new AttachmentDto
                {
                    AttachmentId = attachment.AttachmentId,
                    FileName = attachment.FileName,
                    FileType = attachment.FileType ?? GetMimeType(attachment.FileName),
                    SizeInBytes = attachment.SizeInBytes ?? 0,
                    Url = attachment.Url
                }).ToList(),
                Reactions = (message.Reactions ?? Enumerable.Empty<Reaction>())
                    .GroupBy(reaction => reaction.ReactionType)
                    .Select(group => new ReactionSummaryDto
                    {
                        ReactionType = group.Key,
                        Count = group.Count(),
                        Users = group.Select(reaction => new ReactionUserDto
                        {
                            UserId = reaction.UserId,
                            UserName = reaction.User != null
                                ? $"{reaction.User.FirstName} {reaction.User.LastName}".Trim()
                                : null
                        }).ToList()
                    })
                    .OrderByDescending(reaction => reaction.Count)
                    .ToList(),
                IsPinned = message.IsPinned,
                PinnedAt = message.PinnedAt.HasValue
                    ? UtcDateTime.AsUtc(message.PinnedAt.Value)
                    : null
            }).ToList();

            return new MessageHistoryResult(
                dtos,
                hasMore,
                dtos.Count > 0 ? dtos.Min(dto => dto.SequenceNumber) : null);
        }

        public async Task<MessageHistoryExportResult> ExportChatAsync(
            Guid chatId,
            Guid userId,
            string? format,
            CancellationToken cancellationToken = default)
        {
            var participants = await _chatService.GetChatParticipantsAsync(
                chatId,
                cancellationToken);

            if (!participants.Any(participant => participant.UserId == userId))
            {
                return new MessageHistoryExportResult(
                    MessageHistoryStatus.Forbidden,
                    null);
            }

            var chat = await _chatService.GetChatByIdAsync(chatId, cancellationToken);
            if (chat == null)
            {
                return new MessageHistoryExportResult(
                    MessageHistoryStatus.NotFound,
                    null);
            }

            var normalizedFormat = (format ?? "txt").Trim().ToLowerInvariant();
            if (normalizedFormat is not ("txt" or "json" or "html" or "csv"))
            {
                return new MessageHistoryExportResult(
                    MessageHistoryStatus.InvalidFormat,
                    null);
            }

            var result = await _chatExportService.ExportChatAsync(
                chatId,
                normalizedFormat,
                chat.Name,
                cancellationToken);

            return new MessageHistoryExportResult(MessageHistoryStatus.Success, result);
        }

        private static string GetMimeType(string fileName)
        {
            var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
        }
    }
}
