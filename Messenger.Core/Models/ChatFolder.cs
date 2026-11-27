using System.Text.Json.Serialization;

namespace Messenger.Core.Models;

public class ChatFolder
{
    public Guid FolderId { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    [JsonIgnore]
    public virtual User? User { get; set; }

    [JsonIgnore]
    public virtual ICollection<ChatFolderChat> Chats { get; set; } = new List<ChatFolderChat>();
}
