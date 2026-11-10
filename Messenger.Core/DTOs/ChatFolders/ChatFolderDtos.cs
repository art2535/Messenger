namespace Messenger.Core.DTOs.ChatFolders
{
    public class ChatFolderDto
    {
        public Guid FolderId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public List<Guid> ChatIds { get; set; } = new();
        public int ChatCount => ChatIds?.Count ?? 0;
    }

    public class CreateChatFolderRequest
    {
        public string Name { get; set; } = string.Empty;
        public List<Guid>? ChatIds { get; set; }
    }

    public class UpdateChatFolderRequest
    {
        public string? Name { get; set; }
        public List<Guid>? ChatIds { get; set; }
        public int? SortOrder { get; set; }
    }

    public class FolderChatRequest
    {
        public Guid ChatId { get; set; }
    }
}
