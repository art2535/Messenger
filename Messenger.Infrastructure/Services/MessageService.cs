using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MassTransit;
using Messenger.Core.DTOs;
using Messenger.Core.DTOs.Messages;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Messenger.Infrastructure.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Services
{
    public class MessageService : IMessageService
    {
        private readonly MessageRepository _repository;
        private readonly GuapMessengerContext _context;
        private readonly IEncryptionService _encryptionService;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public MessageService(MessageRepository repository, GuapMessengerContext context, IEncryptionService encryptionService)
        {
            _repository = repository;
            _context = context;
            _encryptionService = encryptionService;
        }

        public async Task<List<MessageDto>> SearchMessagesAsync(Guid chatId, string query, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<MessageDto>();

            query = query.Trim().ToLowerInvariant();

            var (messages, _) = await _repository.GetMessagesByChatIdPagedAsync(chatId, beforeSequence: null, limit: 300, token);

            var filtered = new List<MessageDto>();

            foreach (var m in messages)
            {
                string decryptedText = _encryptionService.TryDecryptSafe(m.MessageText);

                if (decryptedText.ToLowerInvariant().Contains(query))
                {
                    filtered.Add(MapToDto(m, decryptedText));
                }
            }

            return filtered.OrderBy(m => m.SentAt).ToList();
        }

        public async Task<int> MarkMessagesAsReadAsync(Guid chatId, Guid readerId, CancellationToken ct = default)
        {
            var messages = await _context.Messages
                .Where(m => m.ChatId == chatId
                         && m.SenderId != readerId
                         && m.ReadTime == null)
                .ToListAsync(ct);

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            foreach (var msg in messages)
            {
                msg.ReadTime = now;
                msg.DeliveryStatus = MessageDeliveryStatus.Read;
            }

            await _context.SaveChangesAsync(ct);
            return messages.Count;
        }

        public async Task<IEnumerable<Message>> GetMessagesAsync(Guid chatId, CancellationToken token = default)
        {
            var (items, _) = await _repository.GetMessagesByChatIdPagedAsync(chatId, null, 100, token);
            return items;
        }

        public async Task<(IReadOnlyList<Message> Items, bool HasMore)> GetMessagesPagedAsync(
            Guid chatId, long? beforeSequence = null, int limit = 50, CancellationToken token = default)
        {
            return await _repository.GetMessagesByChatIdPagedAsync(chatId, beforeSequence, limit, token);
        }

        public async Task<ServiceResult<Message>> SendMessageAsync(Guid messageId, Guid chatId, Guid senderId,
            string? content, bool hasAttachments, IFormFile[]? files = null, CancellationToken token = default)
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
                    SendTime = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                    DeliveryStatus = MessageDeliveryStatus.Pending
                };

                await _repository.AddMessageAsync(message, token);

                var savedMessage = await _repository.GetMessageByIdAsync(chatId, message.MessageId, token);

                return savedMessage != null
                    ? ServiceResult<Message>.Success(savedMessage)
                    : ServiceResult<Message>.Failure("Не удалось сохранить сообщение");
            }
            catch (Exception ex)
            {
                return ServiceResult<Message>.Failure(ex.Message, ex.InnerException?.Message);
            }
        }

        public async Task<Message?> GetMessageByIdAsync(Guid chatId, Guid messageId, CancellationToken token = default)
        {
            return await _repository.GetMessageByIdAsync(chatId, messageId, token);
        }

        public async Task DeleteMessageAsync(Guid messageId, CancellationToken token = default)
        {
            await _repository.DeleteMessageAsync(messageId, token);
        }

        public async Task UpdateMessageAsync(Message message, CancellationToken token = default)
        {
            await _repository.UpdateMessageAsync(message, token);
        }

        public async Task<ChatExportResult> ExportChatAsync(Guid chatId, string format, string? chatName = null, CancellationToken token = default)
        {
            format = (format ?? "txt").Trim().ToLowerInvariant();
            if (format is not ("txt" or "json" or "html" or "csv"))
                format = "txt";

            var allMessages = new List<Message>();
            long? beforeSeq = null;
            const int pageSize = 200;
            const int maxMessages = 5000;
            bool hasMore = true;

            while (hasMore && allMessages.Count < maxMessages)
            {
                var (batch, more) = await _repository.GetMessagesByChatIdPagedAsync(chatId, beforeSeq, pageSize, token);
                if (batch.Count == 0)
                    break;

                allMessages.InsertRange(0, batch);
                beforeSeq = batch.Min(m => m.SequenceNumber);
                hasMore = more;

                if (batch.Count < pageSize)
                    hasMore = false;
            }

            allMessages = allMessages.OrderBy(m => m.SequenceNumber).ToList();

            var exportItems = allMessages.Select(m =>
            {
                var text = string.IsNullOrEmpty(m.MessageText)
                    ? ""
                    : _encryptionService.TryDecryptSafe(m.MessageText);

                var senderName = m.Sender != null
                    ? $"{m.Sender.FirstName} {m.Sender.LastName}".Trim()
                    : "Пользователь";

                return new
                {
                    MessageId = m.MessageId,
                    SequenceNumber = m.SequenceNumber,
                    SenderId = m.SenderId,
                    SenderName = senderName,
                    Text = text,
                    SentAt = m.SendTime,
                    Status = m.DeliveryStatus.ToString(),
                    HasAttachments = m.HasAttachments,
                    Attachments = m.Attachments?.Select(a => new
                    {
                        a.FileName,
                        a.FileType,
                        Size = a.SizeInBytes,
                        a.Url
                    }).ToList()
                };
            }).ToList();

            var safeName = string.IsNullOrWhiteSpace(chatName)
                ? chatId.ToString("N")[..8]
                : SanitizeFileName(chatName);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

            byte[] content;
            string contentType;
            string fileName;

            switch (format)
            {
                case "json":
                    content = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                    {
                        ChatId = chatId,
                        ChatName = chatName,
                        ExportedAt = DateTime.UtcNow,
                        MessageCount = exportItems.Count,
                        Messages = exportItems
                    }, JsonOptions));
                    contentType = "application/json; charset=utf-8";
                    fileName = $"chat-{safeName}-{timestamp}.json";
                    break;

                case "csv":
                    content = Encoding.UTF8.GetBytes(BuildCsv(exportItems));
                    contentType = "text/csv; charset=utf-8";
                    fileName = $"chat-{safeName}-{timestamp}.csv";
                    break;

                case "html":
                    content = Encoding.UTF8.GetBytes(BuildHtml(chatName ?? chatId.ToString(), exportItems));
                    contentType = "text/html; charset=utf-8";
                    fileName = $"chat-{safeName}-{timestamp}.html";
                    break;

                default:
                    content = Encoding.UTF8.GetBytes(BuildTxt(chatName ?? chatId.ToString(), exportItems));
                    contentType = "text/plain; charset=utf-8";
                    fileName = $"chat-{safeName}-{timestamp}.txt";
                    break;
            }

            return new ChatExportResult
            {
                Content = content,
                ContentType = contentType,
                FileName = fileName,
                Format = format,
                MessageCount = exportItems.Count
            };
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                if (Array.IndexOf(invalid, c) < 0 && c != ' ')
                    sb.Append(c);
                else if (c == ' ')
                    sb.Append('_');
            }
            var result = sb.ToString();
            return string.IsNullOrEmpty(result) ? "chat" : result.Length > 40 ? result[..40] : result;
        }

        private static string BuildTxt(string chatName, IEnumerable<dynamic> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Экспорт чата: {chatName}");
            sb.AppendLine($"Дата экспорта: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine(new string('=', 60));
            sb.AppendLine();

            foreach (var m in items)
            {
                sb.AppendLine($"[{m.SentAt:yyyy-MM-dd HH:mm:ss}] {m.SenderName}:");
                sb.AppendLine(m.Text ?? "");
                if (m.HasAttachments == true && m.Attachments != null)
                {
                    foreach (var a in m.Attachments)
                    {
                        sb.AppendLine($"  📎 {a.FileName} ({a.Size} bytes)");
                    }
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string BuildCsv(IEnumerable<dynamic> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine("SequenceNumber,SentAt,SenderName,SenderId,Text,Status,HasAttachments,AttachmentNames");

            foreach (var m in items)
            {
                var text = EscapeCsv(m.Text?.ToString() ?? "");
                var sender = EscapeCsv(m.SenderName?.ToString() ?? "");
                var attachments = "";
                if (m.Attachments != null)
                {
                    var names = new List<string>();
                    foreach (var a in m.Attachments)
                        names.Add(a.FileName?.ToString() ?? "");
                    attachments = EscapeCsv(string.Join("; ", names));
                }

                sb.AppendLine($"{m.SequenceNumber},{m.SentAt:yyyy-MM-dd HH:mm:ss},{sender},{m.SenderId},{text},{m.Status},{m.HasAttachments},{attachments}");
            }

            return sb.ToString();
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private static string BuildHtml(string chatName, IEnumerable<dynamic> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"ru\">");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset=\"utf-8\">");
            sb.AppendLine($"<title>Экспорт чата: {System.Net.WebUtility.HtmlEncode(chatName)}</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:system-ui,-apple-system,sans-serif;max-width:800px;margin:0 auto;padding:20px;background:#f5f5f5;color:#222}");
            sb.AppendLine("h1{font-size:1.4rem;margin-bottom:4px}");
            sb.AppendLine(".meta{color:#666;font-size:0.9rem;margin-bottom:24px}");
            sb.AppendLine(".msg{background:#fff;border-radius:12px;padding:12px 16px;margin-bottom:10px;box-shadow:0 1px 3px rgba(0,0,0,.08)}");
            sb.AppendLine(".sender{font-weight:600;color:#1a73e8}");
            sb.AppendLine(".time{font-size:0.8rem;color:#888;margin-left:8px}");
            sb.AppendLine(".text{margin-top:6px;white-space:pre-wrap;word-break:break-word}");
            sb.AppendLine(".att{font-size:0.85rem;color:#555;margin-top:4px}");
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine($"<h1>Экспорт чата: {System.Net.WebUtility.HtmlEncode(chatName)}</h1>");
            sb.AppendLine($"<div class=\"meta\">Дата экспорта: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</div>");

            foreach (var m in items)
            {
                sb.AppendLine("<div class=\"msg\">");
                sb.AppendLine($"<span class=\"sender\">{System.Net.WebUtility.HtmlEncode(m.SenderName?.ToString() ?? "")}</span>");
                sb.AppendLine($"<span class=\"time\">{m.SentAt:yyyy-MM-dd HH:mm:ss}</span>");
                sb.AppendLine($"<div class=\"text\">{System.Net.WebUtility.HtmlEncode(m.Text?.ToString() ?? "")}</div>");
                if (m.HasAttachments == true && m.Attachments != null)
                {
                    foreach (var a in m.Attachments)
                    {
                        sb.AppendLine($"<div class=\"att\">📎 {System.Net.WebUtility.HtmlEncode(a.FileName?.ToString() ?? "")}</div>");
                    }
                }
                sb.AppendLine("</div>");
            }

            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        public async Task<int> DeleteMessagesAsync(IEnumerable<Guid> messageIds, CancellationToken token = default)
        {
            var ids = messageIds?.Distinct().ToList() ?? new List<Guid>();
            if (ids.Count == 0) 
                return 0;

            var messages = await _context.Messages
                .Where(m => ids.Contains(m.MessageId))
                .ToListAsync(token);

            if (messages.Count == 0) 
                return 0;

            _context.Messages.RemoveRange(messages);
            await _context.SaveChangesAsync(token);
            return messages.Count;
        }

        private static MessageDto MapToDto(Message m, string decryptedText)
        {
            var reactionGroups = (m.Reactions ?? Enumerable.Empty<Reaction>())
                .GroupBy(r => r.ReactionType)
                .Select(g => new ReactionSummaryDto
                {
                    ReactionType = g.Key,
                    Count = g.Count(),
                    Users = g.Select(r => new ReactionUserDto
                    {
                        UserId = r.UserId,
                        UserName = r.User != null
                            ? $"{r.User.FirstName} {r.User.LastName}".Trim()
                            : null
                    }).ToList()
                })
                .OrderByDescending(x => x.Count)
                .ToList();

            return new MessageDto
            {
                MessageId = m.MessageId,
                ChatId = m.ChatId,
                SenderId = m.SenderId,
                SenderName = m.Sender != null
                    ? $"{m.Sender.FirstName} {m.Sender.LastName}".Trim()
                    : "Пользователь",
                MessageText = decryptedText,
                SentAt = m.SendTime,
                SequenceNumber = m.SequenceNumber,
                Status = m.DeliveryStatus.ToString(),
                Attachments = m.Attachments?.Select(a => new AttachmentDto
                {
                    AttachmentId = a.AttachmentId,
                    FileName = a.FileName,
                    FileType = a.FileType,
                    SizeInBytes = a.SizeInBytes ?? 0,
                    Url = a.Url
                }).ToList() ?? new List<AttachmentDto>(),
                Reactions = reactionGroups
            };
        }
    }
}
