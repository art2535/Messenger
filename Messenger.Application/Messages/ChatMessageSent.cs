using MassTransit;

[MessageUrn("urn:message:Messenger.Core.Messages:ChatMessageSent", useDefaultPrefix: false)]
namespace Messenger.Application.Messages
{
    public record ChatMessageSent
    {
        public Guid MessageId { get; init; }
        public Guid ChatId { get; init; }
        public Guid SenderId { get; init; }
        public string? SenderName { get; init; }
        public string? MessageText { get; init; }
        public DateTime SentAt { get; init; }
        public bool HasAttachments { get; init; }
        public List<AttachmentInfo> Attachments { get; init; } = new();
        public long? SequenceNumber { get; init; }
        public string? ReplyToMessageId { get; init; }
    }
}
