using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Messenger.Core.Models;

public class ChatFolder
{
    [Key]
    [Column("Folder_ID")]
    public Guid FolderId { get; set; }

    [Column("User_ID")]
    public Guid UserId { get; set; }

    [Required]
    [StringLength(64)]
    public string Name { get; set; } = string.Empty;

    [Column("Sort_Order")]
    public int SortOrder { get; set; }

    [Column("Created_At", TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }

    [ForeignKey(nameof(UserId))]
    [JsonIgnore]
    public virtual User? User { get; set; }

    [JsonIgnore]
    public virtual ICollection<ChatFolderChat> Chats { get; set; } = new List<ChatFolderChat>();
}
