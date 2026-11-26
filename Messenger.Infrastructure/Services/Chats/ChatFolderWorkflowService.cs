using Messenger.Core.DTOs.ChatFolders;
using Messenger.Core.Interfaces;
using Messenger.Infrastructure.Interfaces.Chats;

namespace Messenger.Infrastructure.Services.Chats
{
    public sealed class ChatFolderWorkflowService : IChatFolderWorkflowService
    {
        private readonly IChatFolderService _chatFolderService;

        public ChatFolderWorkflowService(IChatFolderService chatFolderService)
        {
            _chatFolderService = chatFolderService;
        }

        public Task<List<ChatFolderDto>> GetFoldersAsync(
            Guid userId,
            CancellationToken ct = default)
        {
            return _chatFolderService.GetFoldersAsync(userId, ct);
        }

        public Task<ChatFolderDto?> GetFolderAsync(
            Guid userId,
            Guid folderId,
            CancellationToken ct = default)
        {
            return _chatFolderService.GetFolderAsync(userId, folderId, ct);
        }

        public Task<ChatFolderDto> CreateFolderAsync(
            Guid userId,
            CreateChatFolderRequest request,
            CancellationToken ct = default)
        {
            return _chatFolderService.CreateFolderAsync(userId, request, ct);
        }

        public Task<ChatFolderDto?> UpdateFolderAsync(
            Guid userId,
            Guid folderId,
            UpdateChatFolderRequest request,
            CancellationToken ct = default)
        {
            return _chatFolderService.UpdateFolderAsync(userId, folderId, request, ct);
        }

        public Task<bool> DeleteFolderAsync(
            Guid userId,
            Guid folderId,
            CancellationToken ct = default)
        {
            return _chatFolderService.DeleteFolderAsync(userId, folderId, ct);
        }

        public Task<bool> AddChatAsync(
            Guid userId,
            Guid folderId,
            Guid chatId,
            CancellationToken ct = default)
        {
            return _chatFolderService.AddChatAsync(userId, folderId, chatId, ct);
        }

        public Task<bool> RemoveChatAsync(
            Guid userId,
            Guid folderId,
            Guid chatId,
            CancellationToken ct = default)
        {
            return _chatFolderService.RemoveChatAsync(userId, folderId, chatId, ct);
        }
    }
}
