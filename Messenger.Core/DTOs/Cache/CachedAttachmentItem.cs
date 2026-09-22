namespace Messenger.Core.DTOs.Cache
{
    public sealed class CachedAttachmentItem
    {
        public Guid AttachmentId { get; set; }
        public string? FileName { get; set; }
        public string? FileType { get; set; }
        public int? SizeInBytes { get; set; }
        public string? Url { get; set; }
    }
}
