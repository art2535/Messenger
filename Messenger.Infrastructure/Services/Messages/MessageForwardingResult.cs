namespace Messenger.Infrastructure.Services.Messages
{
    public enum MessageForwardingFailure
    {
        UserNotFound,
        SourceMessageNotFound,
        SourceChatAccessDenied,
        TargetChatNotFound,
        TargetChatAccessDenied,
        NothingPublished
    }

    public sealed class MessageForwardingResult
    {
        public MessageForwardingFailure? Failure { get; init; }

        public string ErrorMessage { get; init; } = string.Empty;

        public int Published { get; init; }

        public List<string> Errors { get; init; } = new();
    }
}
