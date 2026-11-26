using Messenger.Core.DTOs.ChatFolders;

namespace Messenger.Infrastructure.Interfaces.Chats
{
    public interface IChatFolderWorkflowService
    {
        Task<List<ChatFolderDto>> GetFoldersAsync(
            Guid userId,
            CancellationToken ct = default);

        Task<ChatFolderDto?> GetFolderAsync(
            Guid userId,
            Guid folderId,
            CancellationToken ct = default);

        Task<ChatFolderDto> CreateFolderAsync(
            Guid userId,
            CreateChatFolderRequest request,
            CancellationToken ct = default);

        Task<ChatFolderDto?> UpdateFolderAsync(
            Guid userId,
            Guid folderId,
            UpdateChatFolderRequest request,
            CancellationToken ct = default);

        Task<bool> DeleteFolderAsync(
            Guid userId,
            Guid folderId,
            CancellationToken ct = default);

        Task<bool> AddChatAsync(
            Guid userId,
            Guid folderId,
            Guid chatId,
            CancellationToken ct = default);

        Task<bool> RemoveChatAsync(
            Guid userId,
            Guid folderId,
            Guid chatId,
            CancellationToken ct = default);
    }
}
