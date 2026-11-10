using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Messenger.Core.Models;

public class ChatFolderChat
{
    [Key]
    [Column("Id")]
    public Guid Id { get; set; }

    [Column("Folder_ID")]
    public Guid FolderId { get; set; }

    [Column("Chat_ID")]
    public Guid ChatId { get; set; }

    [ForeignKey(nameof(FolderId))]
    [JsonIgnore]
    public virtual ChatFolder? Folder { get; set; }

    [ForeignKey(nameof(ChatId))]
    [JsonIgnore]
    public virtual Chat? Chat { get; set; }
}
