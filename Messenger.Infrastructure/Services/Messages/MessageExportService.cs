using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Messenger.Core.DTOs;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;

namespace Messenger.Infrastructure.Services.MessageExportService.cs
{
    public sealed class MessageExportService : IMessageExportService
    {
        private readonly IEncryptionService _encryptionService;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public MessageExportService(IEncryptionService encryptionService)
        {
            _encryptionService = encryptionService;
        }

        public ChatExportResult Export(
            Guid chatId,
            string format,
            string? chatName,
            IReadOnlyCollection<Message> allMessages)
        {
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
                    SentAt = Messenger.Core.Models.UtcDateTime.AsUtc(m.SendTime),
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

    }
}