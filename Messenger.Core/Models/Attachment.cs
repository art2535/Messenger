using System.Text.Json.Serialization;

namespace Messenger.Core.Models;

public partial class Attachment
{
    public Guid AttachmentId { get; set; }
    public Guid MessageId { get; set; }
    public string FileName { get; set; } = null!;
    public string? FileType { get; set; }
    public int? SizeInBytes { get; set; }
    public string Url { get; set; } = null!;

    [JsonIgnore]
    public virtual Message Message { get; set; } = null!;
}
