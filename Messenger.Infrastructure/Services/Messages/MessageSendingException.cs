namespace Messenger.Infrastructure.Services.MessageSendingException.cs
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
