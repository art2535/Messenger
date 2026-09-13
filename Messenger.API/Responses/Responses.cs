using Messenger.Core.DTOs.Messages;
using Messenger.Core.Models;

namespace Messenger.API.Responses
{
    public class ExternalLoginRequest
    {
        public string ExternalId { get; set; } = null!;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
    }

    public class RegisterSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public User? User { get; set; }
        public string? Role { get; set; }
        public string? Token { get; set; }
    }

    public class LoginSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public Guid UserId { get; set; }
        public string? Role { get; set; }
        public string? Token { get; set; }
        public string FullName { get; set; } = string.Empty;
    }

    public class LogoutSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class ErrorResponse
    {
        public bool IsSuccess { get; set; }
        public string Error { get; set; } = string.Empty;
    }

    public class GetAttachmentsSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public IEnumerable<Attachment>? Data { get; set; }
    }

    public class GetUserChatsSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public object? Data { get; set; }
    }

    public class GetChatByIdSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public object? Data { get; set; }
    }

    public class CreateChatSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public object? Data { get; set; }
    }

    public class AddParticipantSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class UpdateChatSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class DeleteChatSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class RemoveParticipantSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class CreateAttachmentSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class GetLoginsSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public IEnumerable<Login>? Data { get; set; }
    }

    public class CreateLoginSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class SendMessageSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public MessageDto? Data { get; set; }
    }

    public class GetMessagesSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public List<MessageDto>? Data { get; set; }
        public bool HasMore { get; set; }
        public long? NextBeforeSequence { get; set; }
    }

    public class UpdateMessageStatusSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class GetMessageStatusesSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public object? Data { get; set; }
    }

    public class AddReactionSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class UpdateMessageSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class GetReactionsSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public object? Data { get; set; }
    }

    public class DeleteMessageSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class CreateNotificationSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class GetNotificationsSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public object? Data { get; set; }
    }

    public class DeleteReactionSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class GetUserStatusSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public UserStatus? Data { get; set; }
    }

    public class UpdateUserStatusSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class SearchUsersSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public object? Data { get; set; }
    }

    public class GetAllUsersSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public object? Data { get; set; }
    }

    public class GetRolesSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public object? Data { get; set; }
    }

    public class GetUserNameSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string? Data { get; set; }
    }

    public class GetCurrentUserSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public object? Data { get; set; }
    }

    public class UpdateProfileSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class GetBlockedUsersSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public object? Data { get; set; }
    }

    public class BlockUserSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class UploadAvatarSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public object? Data { get; set; }
    }

    public class UnblockUserSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class ChangePasswordSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class AssignRoleSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class IsBlockedByResponse
    {
        public bool IsBlocked { get; set; }
    }

    public class DeleteAccountSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class DeleteAvatarSuccessResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}