using Messenger.Core.DTOs.Chats;

namespace Messenger.Infrastructure.Interfaces.Chats
{
    public enum ChatManagementStatus
    {
        Success,
        NotFound,
        Forbidden,
        BadRequest
    }

    public sealed record ChatCreationWorkflowResult(Guid ChatId, string Name);

    public sealed record ChatManagementWorkflowResult(
        ChatManagementStatus Status,
        string? Message = null,
        bool UseLegacyErrorResponse = false);

    public interface IChatManagementWorkflowService
    {
        Task<ChatCreationWorkflowResult> CreateChatAsync(
            CreateChatRequest request,
            Guid creatorId,
            CancellationToken ct = default);

        Task<ChatManagementWorkflowResult> AddParticipantAsync(
            Guid chatId,
            Guid userId,
            string role,
            CancellationToken ct = default);

        Task<ChatManagementWorkflowResult> UpdateChatAsync(
            Guid chatId,
            string name,
            CancellationToken ct = default);

        Task<ChatManagementWorkflowResult> DeleteChatAsync(
            Guid chatId,
            CancellationToken ct = default);

        Task<ChatManagementWorkflowResult> RemoveParticipantAsync(
            Guid chatId,
            Guid userId,
            Guid currentUserId,
            CancellationToken ct = default);
    }
}
