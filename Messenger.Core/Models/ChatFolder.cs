
namespace Messenger.Core.Models;

public class ChatFolder
{
    public Guid FolderId { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public virtual User? User { get; set; }
    public virtual ICollection<ChatFolderChat> Chats { get; set; } = new List<ChatFolderChat>();
}
