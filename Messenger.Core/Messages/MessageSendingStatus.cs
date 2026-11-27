namespace Messenger.Core.Messages
{
    public record MessageSendingStatus
    {
        public Guid MessageId { get; init; }
        public Guid ChatId { get; init; }
        public string Status { get; init; } = string.Empty;
        public string? Reason { get; init; }
        public DateTimeOffset Timestamp { get; init; }
    }
}
