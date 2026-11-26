namespace Messenger.Infrastructure.Services
{
    public enum MessageSendingFailure
    {
        UserNotFound,
        ChatNotFound,
        UserNotParticipant
    }

    public sealed class MessageSendingException : Exception
    {
        public MessageSendingFailure Failure { get; }

        public MessageSendingException(MessageSendingFailure failure, string message)
            : base(message)
        {
            Failure = failure;
        }
    }
}
