using System.Text.Json.Serialization;

namespace Messenger.Core.Models;

public class ChatFolderChat
{
    public Guid Id { get; set; }
    public Guid FolderId { get; set; }
    public Guid ChatId { get; set; }

    [JsonIgnore]
    public virtual ChatFolder? Folder { get; set; }

    [JsonIgnore]
    public virtual Chat? Chat { get; set; }
}
