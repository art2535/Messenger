namespace Messenger.Core.DTOs.Messages
{
    public class AttachmentDto
    {
        public Guid AttachmentId { get; set; }
        public string FileName { get; set; } = null!;
        public string FileType { get; set; } = null!;
        public long SizeInBytes { get; set; }
        public string Url { get; set; } = null!;
    }
}
