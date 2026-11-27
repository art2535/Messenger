
namespace Messenger.Core.Models;

public class ChatFolderChat
{
    public Guid Id { get; set; }
    public Guid FolderId { get; set; }
    public Guid ChatId { get; set; }
    public virtual ChatFolder? Folder { get; set; }
    public virtual Chat? Chat { get; set; }
}
