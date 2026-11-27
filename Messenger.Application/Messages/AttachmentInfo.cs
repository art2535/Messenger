namespace Messenger.Application.Messages
{
    public record AttachmentInfo
    {
        public Guid AttachmentId { get; init; }
        public string FileName { get; init; } = string.Empty;
        public string FileType { get; init; } = string.Empty;
        public long SizeInBytes { get; init; }
        public string Url { get; init; } = string.Empty;
    }
}
