namespace Messenger.Core.DTOs.Messages
{
    public class ChatExportResult
    {
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public string ContentType { get; set; } = "application/octet-stream";
        public string FileName { get; set; } = "chat-export.txt";
        public string Format { get; set; } = "txt";
        public int MessageCount { get; set; }
    }
}
